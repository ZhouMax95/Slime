using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Slime
{
    public class SlimeSoftBody : MonoBehaviour
    {

        private static readonly Vector3Int[] StructuralOffsets =
        {
            new Vector3Int(1,0,0),
            new Vector3Int(0,1,0),
            new Vector3Int(0,0,1)
        };

        private static readonly Vector3Int[] ShearOffsets =
        {
            new Vector3Int(1,1,0),
            new Vector3Int(1,-1,0),

            new Vector3Int(1,0,1),
            new Vector3Int(1,0,-1),

            new Vector3Int(0,1,1),
            new Vector3Int(0,1,-1),
        };

        private static readonly Vector3Int[] BodyDiagonalOffsets =
        {
            new Vector3Int(1, 1, 1),
            new Vector3Int(1, 1, -1),
            new Vector3Int(1, -1, 1),
            new Vector3Int(1, -1, -1)
        };


        [Header("Particle")]
        [SerializeField, Range(3, 12)] private int particleGridResolution = 7;
        [SerializeField, Min(0.001f)] private float particleMass = 0.15f;
        [SerializeField, Min(0.001f)] private float particleCollisionRadius = 0.055f;

        [Header("Physics")]
        [SerializeField, Min(0f)] private float gravity = 9.81f;
        [SerializeField, Range(1, 12)] private int solverSubsteps = 6;
        [SerializeField, Min(0f)] private float springStiffness = 70f;
        [SerializeField, Min(0f)] private float springDamping = 1.4f;

        [Header("Collision")]
        [SerializeField]private float groundHeight = 0f;
        [SerializeField,Range(0f,1f)]private float bounce = 0.08f;
        [SerializeField,Range(0f,1f)]private float friction = 0.82f;
        public bool IsGrounded { get; private set; }


        [Header("Debug")]
        [SerializeField] private bool showGeneratedSurface = true;
        [SerializeField] private bool showParticle = true;
        [SerializeField] private bool showSprings;
        [SerializeField] private bool showDensityBounds = true;
        [SerializeField] private bool showCollisionRadius;


        private readonly List<SlimeParticle> particles = new List<SlimeParticle>(256);
        private readonly List<SlimeSpring> springs = new List<SlimeSpring>(1024);
        private readonly Dictionary<Vector3Int, int> gridToParticle = new Dictionary<Vector3Int, int>(512);
        //private readonly OriginDensityField densityField = new OriginDensityField();
        //private readonly OriginMarchingCubes marchingCubes = new OriginMarchingCubes();


        private MeshRenderer sourceRenderer;
        private SphereCollider sourceCollider;

        private Vector3 restCenter;
        private bool initialized;

        public Vector3 Center{ get; private set; }


        private void Start()
        {
            Reinitialize();
            
        }

        private void FixedUpdate()
        {
            if (!initialized)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime / solverSubsteps;
            float substepFriction = Mathf.Pow(friction, 1f / solverSubsteps);
            bool touchedGround = false;

            for (int step = 0; step < solverSubsteps; step++)
            {
                ClearAndApplyExternalForces();
                ApplySpringForces();
                Integrate(deltaTime);
                touchedGround |= SolveGroundCollision(substepFriction);
            }
             IsGrounded = touchedGround;
            Center = CalculateCenter();
        }

        #region physic

        private void ClearAndApplyExternalForces()
        {
            Vector3 acceleration = Vector3.down * gravity;

            for (int i = 0; i < particles.Count; i++)
            {
                SlimeParticle particle = particles[i];
                float mass = 1f / particle.inverseMass;
                particle.force = acceleration * mass;
                particles[i] = particle;
            }
        }

        private void ApplySpringForces()
        {
            for (int i = 0; i < springs.Count; i++)
            {
                SlimeSpring spring = springs[i];

                SlimeParticle particleA = particles[spring.particleA];
                SlimeParticle particleB = particles[spring.particleB];

                Vector3 displacement = particleB.position - particleA.position;
                float length = displacement.magnitude;
                if (length < 0.00001f)
                {
                    continue;
                }
                Vector3 direction = displacement / length;
                float stretch = length - spring.restLength;
                float relativeSpeed = Vector3.Dot(particleB.velocity - particleA.velocity, direction);
                float forceMagnitude = springStiffness * stretch + springDamping * relativeSpeed;
                Vector3 force = direction * forceMagnitude;
                particleA.force += force;
                particleB.force -= force;
                particles[spring.particleA] = particleA;
                particles[spring.particleB] = particleB;
            }
        }

        private void Integrate(float deltaTime)
        {
            for (int i = 0; i < particles.Count; i++)
            {
                SlimeParticle particle = particles[i];
                particle.previousPosition = particle.position;
                Vector3 acceleration = particle.force * particle.inverseMass;

                particle.velocity += acceleration * deltaTime;
                particle.position += particle.velocity * deltaTime;
                particles[i] = particle;
            }
        }

        private bool SolveGroundCollision(float velocityRetention)
        {
            bool touchedGround = false;
            float minimumHeight = groundHeight + particleCollisionRadius;
            for (int i = 0; i < particles.Count; i++)
            {
                SlimeParticle particle = particles[i];
                if (particle.position.y >= minimumHeight)
                {
                    continue;
                }
                touchedGround = true;
                particle.position.y = minimumHeight;
                ResolveCollisionVelocity(ref particle, Vector3.up, velocityRetention);
                particles[i] = particle;
            }
            return touchedGround;
        }

        private void ResolveCollisionVelocity(
            ref SlimeParticle particle,Vector3 surfaceNormal,float velocityRetention)
        {
            float inwardSpeed = Vector3.Dot(particle.velocity, surfaceNormal);
            if (inwardSpeed < 0f)
            {
                particle.velocity -= surfaceNormal * ((1f + bounce) * inwardSpeed);
            }
            Vector3 normalVelocity = surfaceNormal * Vector3.Dot(particle.velocity, surfaceNormal);

            Vector3 tangentVelocity = particle.velocity - normalVelocity;
            particle.velocity = normalVelocity + tangentVelocity * velocityRetention;
        }

        #endregion

        #region particle

        [ContextMenu("Reinitialize Slime")]
        private void Reinitialize()
        {
            initialized = false;
            particles.Clear();
            springs.Clear();
            gridToParticle.Clear();
            sourceRenderer = GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
            {
                sourceRenderer.enabled = false;
            }
            sourceCollider = GetComponent<SphereCollider>();
            GetSourceSphere(sourceCollider, out Vector3 center, out float radius);
            if (sourceCollider != null)
            {
                sourceCollider.enabled = false;
            }

            GenerateParticles(center, radius);
            GenerateSprings();
            
            initialized = particles.Count > 0;
        }

        private void GetSourceSphere(SphereCollider sphere, out Vector3 center, out float radius)
        {
            Vector3 scale = transform.lossyScale;
            float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (sphere != null)
            {
                center = transform.TransformPoint(sphere.center);
                radius = sphere.radius * largestScale;
                return;
            }

            center = transform.position;
            radius = 0.5f * largestScale;
        }

        private void GenerateParticles(Vector3 center, float radius)
        {
            float diameter = radius * 2f;
            float spacing = diameter / (particleGridResolution - 1);
            float inverseMass = 1f / particleMass;

            for (int z = 0; z < particleGridResolution; z++)
            {
                for (int y = 0; y < particleGridResolution; y++)
                {
                    for (int x = 0; x < particleGridResolution; x++)
                    {
                        Vector3 offset = new Vector3(x, y, z) * spacing - Vector3.one * radius;
                        if (offset.sqrMagnitude > radius * radius)
                        {
                            continue;
                        }
                        Vector3 position = center + offset;
                        int particleIndex = particles.Count;
                        particles.Add(new SlimeParticle
                        {
                            position = position,
                            previousPosition = position,
                            velocity = Vector3.zero,
                            force = Vector3.zero,
                            inverseMass = inverseMass,
                            restPosition = position
                        });
                        gridToParticle.Add(new Vector3Int(x, y, z), particleIndex);
                    }
                }
            }

            restCenter = CalculateCenter();
            Center = restCenter;

        }

        private void GenerateSprings()
        {
            foreach (KeyValuePair<Vector3Int, int> entry in gridToParticle)
            {
                Vector3Int gridPosition = entry.Key;
                int particleIndex = entry.Value;
                AddSpringsForOffsets(gridPosition, particleIndex, StructuralOffsets);
                AddSpringsForOffsets(gridPosition, particleIndex, ShearOffsets);
                AddSpringsForOffsets(gridPosition, particleIndex, BodyDiagonalOffsets);
            }
        }

        private void AddSpringsForOffsets(Vector3Int gridPosition, int particleIndex, Vector3Int[] offsets)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3Int neighborGridPosition = gridPosition + offsets[i];
                if (!gridToParticle.TryGetValue(neighborGridPosition, out int neighborIndex))
                {
                    continue;
                }
                
                float restLength = Vector3.Distance(particles[particleIndex].position, particles[neighborIndex].position);
                springs.Add(new SlimeSpring
                {
                    particleA = particleIndex,
                    particleB = neighborIndex,
                    restLength = restLength
                });
            }
        }

        private Vector3 CalculateCenter()
        {
            if (particles.Count == 0)
            {
                return transform.position;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < particles.Count; i++)
            {
                sum += particles[i].position;
            }

            return sum / particles.Count;

        }

        #endregion

        #region GUI
        private void OnDrawGizmos()
        {
            if (!initialized)
            {
                return;
            }

            if (showParticle)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.65f, 0.9f);
                for (int i = 0; i < particles.Count; i++)
                {
                    Gizmos.DrawSphere(particles[i].position, particleCollisionRadius);

                }
            }

            if (showSprings)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.3f);
                for (int i = 0; i < springs.Count; i++)
                {
                    SlimeSpring spring = springs[i];
                    Gizmos.DrawLine(particles[spring.particleA].position, particles[spring.particleB].position);
                }
            }

            // if (showDensityBounds)
            // {
            //     Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.3f);
            //     Gizmos.DrawWireCube(densityField.Bounds.center, densityField.Bounds.size);
            // }

            if (showCollisionRadius)
            {
                Gizmos.color = new Color(1f, 0.45f, 0.15f, 0.8f);
                for (int i = 0; i < particles.Count; i++)
                {
                    Gizmos.DrawWireSphere(particles[i].position, particleCollisionRadius);
                }
            }

        }
        #endregion
    }
}
