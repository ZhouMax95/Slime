using System.Collections.Generic;
using UnityEngine;

namespace SlimeDemo
{
    [DisallowMultipleComponent]
    public sealed class OriginSlimeSoftBody : MonoBehaviour
    {
        private const int MaxOverlappingColliders = 32;
        private const float CollisionSeparationEpsilon = 0.0001f;
        private static readonly Vector3 CollisionProbeParkingPosition = new Vector3(0f, -10000f, 0f);

        private static readonly Vector3Int[] StructuralOffsets =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1)
        };

        private static readonly Vector3Int[] ShearOffsets =
        {
            new Vector3Int(1, 1, 0),
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 0, 1),
            new Vector3Int(1, 0, -1),
            new Vector3Int(0, 1, 1),
            new Vector3Int(0, 1, -1)
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
        [SerializeField] private bool enableVolumePreservation = true;
        [SerializeField, Min(0f)] private float volumeStiffness = 18f;

        [Header("Collision")]
        [SerializeField] private float groundHeight;
        [SerializeField, Range(0f, 1f)] private float bounce = 0.08f;
        [SerializeField, Range(0f, 1f)] private float friction = 0.82f;
        [SerializeField, Range(1, 4)] private int collisionIterations = 2;
        [SerializeField] private LayerMask collidableLayers = Physics.DefaultRaycastLayers;

        [Header("Metaball Surface")]
        [SerializeField, Range(8, 40)] private int densityResolution = 24;
        [SerializeField, Min(0.01f)] private float densityPadding = 0.3f;
        [SerializeField, Min(0.01f)] private float metaballRadius = 0.4f;
        [SerializeField, Min(0.001f)] private float isoLevel = 0.65f;
        [SerializeField, Range(0, 2)] private int densitySmoothingPasses = 1;
        [SerializeField, Range(1, 8)] private int meshUpdateInterval = 1;
        [SerializeField] private Material slimeMaterial;

        [Header("Debug")]
        [SerializeField] private bool showGeneratedSurface = true;
        [SerializeField] private bool showParticles = true;
        [SerializeField] private bool showSprings;
        [SerializeField] private bool showDensityBounds = true;
        [SerializeField] private bool showCollisionRadius;

        private readonly List<OriginSlimeParticle> particles = new List<OriginSlimeParticle>(256);
        private readonly List<OriginSlimeSpring> springs = new List<OriginSlimeSpring>(1024);
        private readonly Dictionary<Vector3Int, int> gridToParticle = new Dictionary<Vector3Int, int>(512);
        private readonly Collider[] overlappingColliders = new Collider[MaxOverlappingColliders];
        private readonly OriginDensityField densityField = new OriginDensityField();
        private readonly OriginMarchingCubes marchingCubes = new OriginMarchingCubes();

        private MeshRenderer sourceRenderer;
        private SphereCollider sourceCollider;
        private SphereCollider particleCollisionProbe;
        private MeshFilter generatedMeshFilter;
        private MeshRenderer generatedMeshRenderer;
        private Mesh generatedMesh;
        private Material fallbackMaterial;
        private Vector3 restCenter;
        private Vector3 desiredHorizontalVelocity;
        private float driveResponsiveness;
        private float maximumDriveAcceleration;
        private float requestedJumpSpeed;
        private bool jumpRequested;
        private bool initialized;

        public IReadOnlyList<OriginSlimeParticle> Particles => particles;
        public IReadOnlyList<OriginSlimeSpring> Springs => springs;
        public Vector3 Center { get; private set; }
        public bool IsGrounded { get; private set; }
        public int GeneratedVertexCount => marchingCubes.VertexCount;
        public int GeneratedTriangleCount => marchingCubes.TriangleCount;
        public Bounds DensityBounds => densityField.Bounds;

        public Vector3 AverageVelocity
        {
            get
            {
                if (particles.Count == 0)
                {
                    return Vector3.zero;
                }

                Vector3 sum = Vector3.zero;
                for (int i = 0; i < particles.Count; i++)
                {
                    sum += particles[i].velocity;
                }

                return sum / particles.Count;
            }
        }

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

            ApplyRequestedJump();

            float substepDeltaTime = Time.fixedDeltaTime / solverSubsteps;
            float substepFriction = Mathf.Pow(friction, 1f / solverSubsteps);
            Vector3 driveAcceleration = CalculateDriveAcceleration();
            bool touchedGround = false;

            for (int step = 0; step < solverSubsteps; step++)
            {
                ClearAndApplyExternalForces(driveAcceleration);
                ApplySpringForces();

                if (enableVolumePreservation)
                {
                    ApplyVolumePreservation();
                }

                Integrate(substepDeltaTime);
                touchedGround |= SolveGroundCollision(substepFriction);
                touchedGround |= SolveColliderCollisions(substepFriction);
            }

            IsGrounded = touchedGround;
            Center = CalculateCenter();
        }

        private void LateUpdate()
        {
            if (!initialized || Time.frameCount % meshUpdateInterval != 0)
            {
                return;
            }

            RebuildSurface();
        }

        [ContextMenu("Reinitialize Slime")]
        public void Reinitialize()
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

            EnsureCollisionProbe();

            GenerateParticles(center, radius);
            GenerateSprings();

            desiredHorizontalVelocity = Vector3.zero;
            jumpRequested = false;
            IsGrounded = false;
            initialized = particles.Count > 0;

            EnsureGeneratedSurface();
            RebuildSurface();

            Debug.Log($"Slime initialized with {particles.Count} particles and {springs.Count} springs.", this);
        }

        public void SetDriveTarget(Vector3 horizontalVelocity, float responsiveness, float maxAcceleration)
        {
            horizontalVelocity.y = 0f;
            desiredHorizontalVelocity = horizontalVelocity;
            driveResponsiveness = Mathf.Max(0f, responsiveness);
            maximumDriveAcceleration = Mathf.Max(0f, maxAcceleration);
        }

        public void RequestJump(float jumpSpeed)
        {
            requestedJumpSpeed = Mathf.Max(0f, jumpSpeed);
            jumpRequested = true;
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
                        particles.Add(new OriginSlimeParticle
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
                AddSpringsForOffsets(entry.Key, entry.Value, StructuralOffsets);
                AddSpringsForOffsets(entry.Key, entry.Value, ShearOffsets);
                AddSpringsForOffsets(entry.Key, entry.Value, BodyDiagonalOffsets);
            }
        }

        private void AddSpringsForOffsets(Vector3Int gridPosition, int particleIndex, Vector3Int[] offsets)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!gridToParticle.TryGetValue(gridPosition + offsets[i], out int neighborIndex))
                {
                    continue;
                }

                springs.Add(new OriginSlimeSpring
                {
                    particleA = particleIndex,
                    particleB = neighborIndex,
                    restLength = Vector3.Distance(particles[particleIndex].position, particles[neighborIndex].position),
                    stiffness = springStiffness,
                    damping = springDamping
                });
            }
        }

        private Vector3 CalculateDriveAcceleration()
        {
            Vector3 currentVelocity = AverageVelocity;
            currentVelocity.y = 0f;
            Vector3 acceleration = (desiredHorizontalVelocity - currentVelocity) * driveResponsiveness;
            return Vector3.ClampMagnitude(acceleration, maximumDriveAcceleration);
        }

        private void ApplyRequestedJump()
        {
            if (!jumpRequested)
            {
                return;
            }

            jumpRequested = false;
            if (!IsGrounded)
            {
                return;
            }

            for (int i = 0; i < particles.Count; i++)
            {
                OriginSlimeParticle particle = particles[i];
                particle.velocity.y = Mathf.Max(particle.velocity.y, requestedJumpSpeed);
                particles[i] = particle;
            }

            IsGrounded = false;
        }

        private void ClearAndApplyExternalForces(Vector3 driveAcceleration)
        {
            Vector3 acceleration = Vector3.down * gravity + driveAcceleration;

            for (int i = 0; i < particles.Count; i++)
            {
                OriginSlimeParticle particle = particles[i];
                float mass = 1f / particle.inverseMass;
                particle.force = acceleration * mass;
                particles[i] = particle;
            }
        }

        private void ApplySpringForces()
        {
            for (int i = 0; i < springs.Count; i++)
            {
                OriginSlimeSpring spring = springs[i];
                OriginSlimeParticle particleA = particles[spring.particleA];
                OriginSlimeParticle particleB = particles[spring.particleB];
                Vector3 displacement = particleB.position - particleA.position;
                float length = displacement.magnitude;

                if (length < 0.00001f)
                {
                    continue;
                }

                Vector3 direction = displacement / length;
                float stretch = length - spring.restLength;
                float relativeSpeed = Vector3.Dot(particleB.velocity - particleA.velocity, direction);

                // Hooke force restores the rest length; damping removes motion along the spring.
                float forceMagnitude = springStiffness * stretch + springDamping * relativeSpeed;
                Vector3 force = direction * forceMagnitude;

                particleA.force += force;
                particleB.force -= force;
                particles[spring.particleA] = particleA;
                particles[spring.particleB] = particleB;
            }
        }

        private void ApplyVolumePreservation()
        {
            Vector3 currentCenter = CalculateCenter();

            for (int i = 0; i < particles.Count; i++)
            {
                OriginSlimeParticle particle = particles[i];
                Vector3 restOffset = particle.restPosition - restCenter;
                Vector3 currentOffset = particle.position - currentCenter;

                // This is a deliberately simple shape-matching force, not a strict volume constraint.
                // The center can move freely while each particle gently restores the initial blob shape.
                particle.force += (restOffset - currentOffset) * volumeStiffness;
                particles[i] = particle;
            }
        }

        private void Integrate(float deltaTime)
        {
            for (int i = 0; i < particles.Count; i++)
            {
                OriginSlimeParticle particle = particles[i];
                particle.previousPosition = particle.position;
                particle.velocity += particle.force * particle.inverseMass * deltaTime;
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
                OriginSlimeParticle particle = particles[i];
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

        private bool SolveColliderCollisions(float velocityRetention)
        {
            if (particleCollisionProbe == null || collidableLayers.value == 0)
            {
                return false;
            }

            bool touchedWalkableSurface = false;
            for (int iteration = 0; iteration < collisionIterations; iteration++)
            {
                bool resolvedAnyCollision = false;

                for (int particleIndex = 0; particleIndex < particles.Count; particleIndex++)
                {
                    OriginSlimeParticle particle = particles[particleIndex];
                    int overlapCount = Physics.OverlapSphereNonAlloc(
                        particle.position,
                        particleCollisionRadius,
                        overlappingColliders,
                        collidableLayers,
                        QueryTriggerInteraction.Ignore);

                    for (int colliderIndex = 0; colliderIndex < overlapCount; colliderIndex++)
                    {
                        Collider other = overlappingColliders[colliderIndex];
                        if (ShouldIgnoreCollider(other))
                        {
                            continue;
                        }

                        bool overlaps = Physics.ComputePenetration(
                            particleCollisionProbe,
                            particle.position,
                            Quaternion.identity,
                            other,
                            other.transform.position,
                            other.transform.rotation,
                            out Vector3 separationDirection,
                            out float separationDistance);
                        if (!overlaps || separationDistance <= 0f)
                        {
                            continue;
                        }

                        particle.position += separationDirection
                            * (separationDistance + CollisionSeparationEpsilon);
                        ResolveCollisionVelocity(ref particle, separationDirection, velocityRetention);
                        touchedWalkableSurface |= separationDirection.y > 0.5f;
                        resolvedAnyCollision = true;
                    }

                    particles[particleIndex] = particle;
                }

                if (!resolvedAnyCollision)
                {
                    break;
                }
            }

            return touchedWalkableSurface;
        }

        private bool ShouldIgnoreCollider(Collider other)
        {
            if (other == null || other == sourceCollider || other == particleCollisionProbe)
            {
                return true;
            }

            Transform otherTransform = other.transform;
            return otherTransform == transform || otherTransform.IsChildOf(transform);
        }

        private void ResolveCollisionVelocity(
            ref OriginSlimeParticle particle,
            Vector3 surfaceNormal,
            float velocityRetention)
        {
            float inwardSpeed = Vector3.Dot(particle.velocity, surfaceNormal);
            if (inwardSpeed < 0f)
            {
                particle.velocity -= surfaceNormal * ((1f + bounce) * inwardSpeed);
            }

            Vector3 normalVelocity = surfaceNormal
                * Vector3.Dot(particle.velocity, surfaceNormal);
            Vector3 tangentVelocity = particle.velocity - normalVelocity;
            particle.velocity = normalVelocity + tangentVelocity * velocityRetention;
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

        private void EnsureGeneratedSurface()
        {
            const string surfaceName = "Generated Surface";
            Transform surfaceTransform = transform.Find(surfaceName);
            if (surfaceTransform == null)
            {
                GameObject surfaceObject = new GameObject(surfaceName);
                surfaceTransform = surfaceObject.transform;
                surfaceTransform.SetParent(transform, false);
            }

            surfaceTransform.localPosition = Vector3.zero;
            surfaceTransform.localRotation = Quaternion.identity;
            surfaceTransform.localScale = Vector3.one;

            generatedMeshFilter = surfaceTransform.GetComponent<MeshFilter>();
            if (generatedMeshFilter == null)
            {
                generatedMeshFilter = surfaceTransform.gameObject.AddComponent<MeshFilter>();
            }

            generatedMeshRenderer = surfaceTransform.GetComponent<MeshRenderer>();
            if (generatedMeshRenderer == null)
            {
                generatedMeshRenderer = surfaceTransform.gameObject.AddComponent<MeshRenderer>();
            }

            if (generatedMesh == null)
            {
                generatedMesh = new Mesh
                {
                    name = "SlimeGeneratedMesh"
                };
                generatedMesh.MarkDynamic();
            }

            generatedMeshFilter.sharedMesh = generatedMesh;
            generatedMeshRenderer.enabled = showGeneratedSurface;
            generatedMeshRenderer.sharedMaterial = slimeMaterial != null
                ? slimeMaterial
                : GetFallbackMaterial();
        }

        private void EnsureCollisionProbe()
        {
            if (particleCollisionProbe != null)
            {
                particleCollisionProbe.radius = particleCollisionRadius;
                return;
            }

            GameObject probeObject = new GameObject("Slime Particle Collision Probe")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = 2
            };
            probeObject.transform.position = CollisionProbeParkingPosition;
            particleCollisionProbe = probeObject.AddComponent<SphereCollider>();
            particleCollisionProbe.radius = particleCollisionRadius;
            particleCollisionProbe.isTrigger = true;
        }

        private void RebuildSurface()
        {
            if (particles.Count == 0)
            {
                return;
            }

            EnsureGeneratedSurface();
            densityField.Rebuild(
                particles,
                densityResolution,
                densityPadding,
                metaballRadius,
                densitySmoothingPasses);
            marchingCubes.BuildMesh(
                densityField,
                isoLevel,
                transform.worldToLocalMatrix,
                generatedMesh);
        }

        private Material GetFallbackMaterial()
        {
            if (fallbackMaterial != null)
            {
                return fallbackMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            fallbackMaterial = new Material(shader)
            {
                name = "Slime Runtime Material"
            };
            fallbackMaterial.SetColor("_BaseColor", new Color(0.12f, 0.82f, 0.52f, 1f));
            fallbackMaterial.SetFloat("_Metallic", 0f);
            fallbackMaterial.SetFloat("_Smoothness", 0.82f);
            return fallbackMaterial;
        }

        private void OnDestroy()
        {
            if (particleCollisionProbe != null)
            {
                Destroy(particleCollisionProbe.gameObject);
            }

            if (generatedMesh != null)
            {
                Destroy(generatedMesh);
            }

            if (fallbackMaterial != null)
            {
                Destroy(fallbackMaterial);
            }
        }

        private void OnDrawGizmos()
        {
            if (!initialized)
            {
                return;
            }

            if (showParticles)
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
                    OriginSlimeSpring spring = springs[i];
                    Gizmos.DrawLine(particles[spring.particleA].position, particles[spring.particleB].position);
                }
            }

            if (showDensityBounds)
            {
                Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.8f);
                Gizmos.DrawWireCube(densityField.Bounds.center, densityField.Bounds.size);
            }

            if (showCollisionRadius)
            {
                Gizmos.color = new Color(1f, 0.45f, 0.15f, 0.8f);
                for (int i = 0; i < particles.Count; i++)
                {
                    Gizmos.DrawWireSphere(particles[i].position, particleCollisionRadius);
                }
            }
        }
    }
}
