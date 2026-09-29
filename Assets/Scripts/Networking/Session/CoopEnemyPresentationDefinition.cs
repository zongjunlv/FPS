using UnityEngine;

namespace FPS.Networking.Session
{
    /// <summary>Read-only visual calibration shared by presentation and server content conversion.</summary>
    [DisallowMultipleComponent]
    public sealed class CoopEnemyPresentationDefinition : MonoBehaviour
    {
        public string SourceAddress;
        public Vector3 BodyCenter;
        public Vector3 BodyHalfExtents;
        public Vector3 HeadCenter;
        public Vector3 HeadHalfExtents;
        public Vector3 StatusAnchor;
        public bool HasLocomotionClip;
        public float LocomotionReferenceSpeed = 2.4f;
    }
}
