using Mujoco;
using UnityEngine;
/// <summary>Explicit measured state. Angles are radians; velocity is radians/second.
/// Assign the physical hinge directly; do not infer measurement from controller commands.</summary>
public class HandJointState : MonoBehaviour
{
    public DOA axis;
    public MjHingeJoint joint;
    public double PositionRad => joint == null ? double.NaN : joint.RawConfiguration;
    public float VelocityRadPerSecond => joint == null ? float.NaN : joint.Velocity;
}
