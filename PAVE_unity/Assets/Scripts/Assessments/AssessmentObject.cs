using Mujoco;
using UnityEngine;

namespace Pave.Assessments
{
    /// <summary>Explicit scene references for one scored prop; no grab or snapping behavior.
    /// Native MuJoCo owns every moving body. Target is a geometric scoring reference only.</summary>
    public sealed class AssessmentObject : MonoBehaviour
    {
        public int Index;
        public MjBody Body;
        public MjFreeJoint FreeJoint;
        public MjGeom[] Geoms;
        public Transform Target;
        public MjHingeJoint Jaw;
        public float InitialJawDegrees;
        public Vector3 InitialPosition;
        public Quaternion InitialRotation;
    }
}
