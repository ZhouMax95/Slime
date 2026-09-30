using System;
using UnityEngine;

namespace SlimeDemo
{
    [Serializable]
    public struct OriginSlimeParticle
    {
        public Vector3 position;
        public Vector3 previousPosition;
        public Vector3 velocity;
        public Vector3 force;
        public float inverseMass;
        public Vector3 restPosition;
    }
}
