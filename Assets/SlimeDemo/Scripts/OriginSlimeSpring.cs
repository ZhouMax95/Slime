using System;

namespace SlimeDemo
{
    [Serializable]
    public struct OriginSlimeSpring
    {
        public int particleA;
        public int particleB;
        public float restLength;
        public float stiffness;
        public float damping;
    }
}
