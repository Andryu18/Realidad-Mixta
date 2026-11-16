namespace RallyMixto
{
    public struct AutoStep
    {
        public string name;
        public float duration;
        public int a, b;
    }

    // misma rutina que el firmware del carro
    public static class AutoRoutine
    {
        public static readonly AutoStep[] Steps =
        {
            new AutoStep { name = "Espera",         duration = 3f,   a = 0,    b = 0 },
            new AutoStep { name = "Avanza",         duration = 5f,   a = 254,  b = 254 },
            new AutoStep { name = "Pausa",          duration = 1f,   a = 0,    b = 0 },
            new AutoStep { name = "Gira derecha",   duration = 2.5f, a = 254,  b = -254 },
            new AutoStep { name = "Pausa",          duration = 1f,   a = 0,    b = 0 },
            new AutoStep { name = "Avanza",         duration = 5f,   a = 254,  b = 254 },
            new AutoStep { name = "Pausa",          duration = 1f,   a = 0,    b = 0 },
            new AutoStep { name = "Gira izquierda", duration = 2.5f, a = -254, b = 254 },
            new AutoStep { name = "Pausa",          duration = 1f,   a = 0,    b = 0 },
            new AutoStep { name = "Retrocede",      duration = 3f,   a = -254, b = -254 },
        };

        public static float Total
        {
            get
            {
                float t = 0;
                foreach (var s in Steps) t += s.duration;
                return t;
            }
        }
    }
}
