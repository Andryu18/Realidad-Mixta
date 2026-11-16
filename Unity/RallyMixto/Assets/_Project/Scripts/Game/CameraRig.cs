using UnityEngine;

namespace RallyMixto
{
    public enum CamMode { Showcase, Chase, Overview }

    // camara: vitrina, persecucion y vista aerea
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public CamMode mode = CamMode.Showcase;
        public float orbitSpeed = 10f;
        public Vector2 showcaseOffset = new Vector2(0, 0);

        Camera cam;
        float orbit = 210f, shake;
        Vector3 vel;

        void Awake() { cam = GetComponent<Camera>(); }

        public void Shake(float amount) { shake = Mathf.Max(shake, amount); }

        public void Snap()
        {
            Desired(out var p, out var look);
            transform.position = p;
            transform.LookAt(look);
            vel = Vector3.zero;
        }

        void Desired(out Vector3 pos, out Vector3 look)
        {
            var t = target.position;
            switch (mode)
            {
                case CamMode.Chase:
                    var fwd = target.forward;
                    pos = t - fwd * 7.5f + Vector3.up * 3.6f;
                    look = t + fwd * 3.5f + Vector3.up * 0.4f;
                    break;
                case CamMode.Overview:
                    pos = t + new Vector3(-5f, 7.5f, -8f);
                    look = t + Vector3.up * 0.3f;
                    break;
                default:
                    var dir = Quaternion.Euler(0, orbit, 0) * Vector3.forward;
                    pos = t + dir * 4.4f + Vector3.up * 1.6f;
                    var right = Vector3.Cross(Vector3.up, -dir).normalized;
                    look = t + Vector3.up * 0.45f - right * showcaseOffset.x + Vector3.up * showcaseOffset.y;
                    break;
            }
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;
            if (mode == CamMode.Showcase) orbit += orbitSpeed * dt;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, mode == CamMode.Chase ? 58f : 45f, dt * 2f);

            Desired(out var p, out var look);
            float smooth = mode == CamMode.Chase ? 0.18f : 0.45f;
            transform.position = Vector3.SmoothDamp(transform.position, p, ref vel, smooth, Mathf.Infinity, dt);
            var rot = Quaternion.LookRotation(look - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, dt * 8f);

            if (shake > 0)
            {
                transform.position += Random.insideUnitSphere * shake * 0.25f;
                shake = Mathf.MoveTowards(shake, 0, dt * 2.5f);
            }
        }
    }
}
