using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Delvefolk
{
    // Esqueleto de un personaje personalizado en partida (solo visual):
    //  1. reproduce su animación de correr según la velocidad (quieto: su fotograma más neutro)
    //  2. después de la animación, coloca el brazo del arma con IK de dos huesos hacia donde apunta el pivote del
    //     brazo del juego (las mismas poses de ataque que el aventurero clásico)
    //  3. el socket del arma es hijo del hueso de la mano: el arma sigue a la mano y se orienta con la pose.
    // Los huesos se resuelven una vez al montar el modelo (nunca se buscan cada frame).
    public sealed class HeroRig : MonoBehaviour
    {
        public HeroEntry Entry;
        public Transform Pivot;          // CharacterParts.ArmPivot: su rotación es la pose del brazo
        public Transform ModelRoot;      // el "Modelo" del jugador (espacio local del personaje)
        public Transform Upper, Fore, Hand, Socket;
        public float Moving;             // 0..1 (velocidad / velocidad base)
        public float Reach;              // pose.Reach del juego
        public bool ArmActive;           // lleva algo, ataca o carga: el brazo sigue la pose
        public string WeaponId;
        public bool Valid { get { return Upper && Fore && Hand && Socket; } }

        Animator anim;
        PlayableGraph graph;
        AnimationClipPlayable clipPlay, idlePlay;
        AnimationMixerPlayable mixer;
        float clipT, idleT, idleClipT, runW, armBlend;
        float upperLen, foreLen;

        public void Init(HeroEntry e, Transform pivot, Transform modelRoot, Animator a, Transform upper, Transform fore, Transform hand, Transform socket, float idleTime)
        {
            Entry = e; Pivot = pivot; ModelRoot = modelRoot; anim = a;
            Upper = upper; Fore = fore; Hand = hand; Socket = socket;
            idleT = clipT = idleTime;
            if (Valid)
            {
                upperLen = Vector3.Distance(upper.position, fore.position);
                foreLen = Vector3.Distance(fore.position, hand.position);
            }
            if (anim && e.Run)
            {
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Heroe_" + e.Id);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                // Mezcla reposo ↔ correr según la velocidad (sin Idle: el fotograma neutro del correr)
                mixer = AnimationMixerPlayable.Create(graph, 2);
                clipPlay = AnimationClipPlayable.Create(graph, e.Run);
                clipPlay.SetApplyFootIK(false);
                graph.Connect(clipPlay, 0, mixer, 0);
                if (e.Idle)
                {
                    idlePlay = AnimationClipPlayable.Create(graph, e.Idle);
                    idlePlay.SetApplyFootIK(false);
                    graph.Connect(idlePlay, 0, mixer, 1);
                }
                var output = AnimationPlayableOutput.Create(graph, "Salida", anim);
                output.SetSourcePlayable(mixer);
                clipPlay.SetTime(clipT);
                SetWeights();
                graph.Play();
                graph.Evaluate(0f);
            }
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        void LateUpdate() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (graph.IsValid() && anim && anim.isActiveAndEnabled)
            {
                float len = Mathf.Max(0.01f, Entry.Run.length);
                bool moving = Moving > 0.05f;
                if (moving) clipT += dt * Mathf.Clamp(Moving, 0.5f, 1.6f);
                else if (!Entry.Idle) clipT = Mathf.MoveTowards(clipT, NearestIdle(clipT, len), dt * 1.5f);   // se recoloca al fotograma neutro
                clipPlay.SetTime(clipT % len);
                if (Entry.Idle)
                {
                    idleClipT += dt;
                    idlePlay.SetTime(idleClipT % Mathf.Max(0.01f, Entry.Idle.length));
                    runW = Mathf.MoveTowards(runW, moving ? 1f : 0f, dt * 6f);   // transición suave (≈0,17 s)
                }
                SetWeights();
                graph.Evaluate(0f);
            }
            if (!Valid) return;
            armBlend = Mathf.MoveTowards(armBlend, ArmActive ? 1f : 0f, dt * 8f);
            if (armBlend > 0.001f) SolveArm(armBlend);
            PlaceSocket();
        }

        void SetWeights()
        {
            if (!mixer.IsValid()) return;
            float w = Entry.Idle ? runW : 1f;
            mixer.SetInputWeight(0, w);
            mixer.SetInputWeight(1, Entry.Idle ? 1f - w : 0f);
        }

        // Estado de la animación (pruebas): peso del correr (0 = reposo puro)
        public float RunWeight { get { return Entry != null && Entry.Idle ? runW : 1f; } }
        public bool HasIdle { get { return Entry != null && Entry.Idle; } }

        // El fotograma neutro más cercano (hacia delante) para que no dé un salto al pararse
        float NearestIdle(float t, float len)
        {
            float k = Mathf.Floor(t / len) * len + idleT;
            if (k < t - 0.001f) k += len;
            return k;
        }

        // Dirección del brazo en el espacio del personaje; la mano izquierda usa la pose reflejada
        Vector3 Dir(Vector3 worldDir)
        {
            if (Entry.Hand == HeroHand.Right) return worldDir;
            var l = ModelRoot.InverseTransformDirection(worldDir); l.x = -l.x;
            return ModelRoot.TransformDirection(l);
        }

        // IK analítico de dos huesos: la mano va hacia el pivote · (largo del brazo), con el codo hacia abajo y atrás
        void SolveArm(float w)
        {
            Vector3 a = Upper.position, b = Fore.position, c = Hand.position;
            var fwd = Dir(Pivot.forward);
            float lab = upperLen, lcb = foreLen;
            float reach = Mathf.Clamp((lab + lcb) * (0.86f + Reach * 0.25f), 0.05f, (lab + lcb) * 0.999f);
            var t = Vector3.Lerp(c, a + fwd * reach, w);
            float lat = Mathf.Clamp((t - a).magnitude, 0.01f, lab + lcb - 0.001f);
            var pole = -ModelRoot.up * 0.8f - ModelRoot.forward * 0.4f + (Entry.Hand == HeroHand.Right ? ModelRoot.right : -ModelRoot.right) * 0.3f;
            var axis = Vector3.Cross(t - a, pole);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            float acab0 = Angle(c - a, b - a), babc0 = Angle(a - b, c - b);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            // Coloca el codo en el plano del polo y ajusta los dos ángulos (ley de cosenos)
            var bendNow = Vector3.Cross(c - a, b - a);
            if (bendNow.sqrMagnitude > 1e-8f) Upper.rotation = Quaternion.FromToRotation(bendNow.normalized, axis) * Upper.rotation;
            Upper.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * Upper.rotation;
            Fore.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis) * Fore.rotation;
            Upper.rotation = Quaternion.FromToRotation(Hand.position - Upper.position, t - Upper.position) * Upper.rotation;
        }

        static float Angle(Vector3 u, Vector3 v)
        {
            if (u.sqrMagnitude < 1e-10f || v.sqrMagnitude < 1e-10f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
        }

        // El socket (hijo del hueso de la mano) sigue la posición de la mano y se orienta con la pose del brazo
        void PlaceSocket()
        {
            var fwd = Dir(Pivot.forward);
            var up = Dir(Pivot.up);
            var rot = Quaternion.LookRotation(fwd, up);
            Vector3 off = Entry.SocketOffset, eul = Entry.SocketEuler;
            float sc = Entry.SocketScale;
            var wo = Entry.OffsetFor(WeaponId);
            if (wo != null) { off += wo.Offset; eul += wo.Euler; sc *= wo.Scale; }
            Socket.position = Hand.position + rot * off * ModelRoot.lossyScale.x;
            Socket.rotation = rot * Quaternion.Euler(eul);
            var ps = Hand.lossyScale.x;
            Socket.localScale = Vector3.one * (sc * ModelRoot.lossyScale.x / Mathf.Max(1e-5f, ps));
        }
    }
}
