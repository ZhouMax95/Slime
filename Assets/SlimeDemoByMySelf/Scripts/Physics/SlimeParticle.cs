using System;
using UnityEngine;

namespace Slime
{
    [Serializable]
    public struct SlimeParticle{
        public Vector3 position;
        public Vector3 previousPosition;
        public Vector3 velocity;
        public Vector3 force;
        public float inverseMass;
        public Vector3 restPosition;
    }
}

