using UnityEngine;

namespace RallyMixto
{
    // materiales urp creados en el editor (asi entran al build)
    [CreateAssetMenu(menuName = "Rally Mixto/Material Library")]
    public class MaterialLibrary : ScriptableObject
    {
        [Header("carro")]
        public Material chassis;
        public Material rubber;
        public Material motor;
        public Material pcb;
        public Material pcbAlt;
        public Material battery;
        public Material driver;
        public Material metal;
        public Material ledOn;
        public Material brakeLight;

        [Header("arena")]
        public Material floor;
        public Material wall;
        public Material wallTop;
        public Material neonCyan;
        public Material neonAmber;
        public Material neonRed;
        public Material boost;
        public Material oil;
        public Material cell;
        public Material pad;
        public Material checker;
        public Material dark;

        [Header("fx")]
        public Material particle;
        public Material glowRing;

        [Header("modelo")]
        public GameObject carModel;
    }
}
