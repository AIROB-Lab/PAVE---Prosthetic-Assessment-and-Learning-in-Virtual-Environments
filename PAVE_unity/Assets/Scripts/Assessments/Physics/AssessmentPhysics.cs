using System;
using System.Collections.Generic;
using Mujoco;
using UnityEngine;

namespace Pave.Assessments
{
    /// <summary>A fresh native-contact observation. Forces are sums of hand/object contact
    /// normal magnitudes in newtons, not a calibrated grip force or a running accumulator.</summary>
    public struct AssessmentContact
    {
        public bool Hand;
        public bool SourceSupport;
        public bool TargetSupport;
        public bool LowerJawOnTarget;
        public bool UpperJawOnTarget;
        public double HandNormalForce;
    }

    /// <summary>Small native boundary for new assessment scenes only. It observes MuJoCo
    /// after integration and can reset props between trials; it never attaches props to a hand.</summary>
    public static unsafe class AssessmentPhysics
    {
        public static bool Ready(MjScene scene) => scene != null && scene.Model != null && scene.Data != null;
        public static double Time(MjScene scene) => Ready(scene) ? scene.Data->time : double.NaN;
        public static double Step(MjScene scene) => Ready(scene) ? scene.Model->opt.timestep : double.NaN;
        public static double Mass(MjScene scene, MjBody body) => scene.Model->body_subtreemass[body.MujocoId];
        public static Vector3 Velocity(MjScene scene, MjFreeJoint joint) => MjEngineTool.UnityVector3(scene.Data->qvel + joint.DofAddress);
        public static Vector3 AngularVelocity(MjScene scene, MjFreeJoint joint) => MjEngineTool.UnityVector3(scene.Data->qvel + joint.DofAddress + 3);
        public static void ReadContacts(MjScene scene, HashSet<int> handGeoms, Dictionary<int, int> objectGeoms, int[] rootBodyIds, HashSet<int> sourceGeoms, HashSet<int> targetGeoms, AssessmentContact[] observations)
        {
            Array.Clear(observations, 0, observations.Length);
            double* force = stackalloc double[6];
            for (int index = 0; index < scene.Data->ncon; index++)
            {
                var contact = scene.Data->contact[index];
                ReadPair(contact.geom1, contact.geom2, index);
                ReadPair(contact.geom2, contact.geom1, index);
            }

            void ReadPair(int itemGeom, int otherGeom, int contactIndex)
            {
                if (!objectGeoms.TryGetValue(itemGeom, out int item) || otherGeom < 0)
                {
                    return;
                }

                ref AssessmentContact observation = ref observations[item];
                if (handGeoms.Contains(otherGeom))
                {
                    observation.Hand = true;
                    MujocoLib.mj_contactForce(scene.Model, scene.Data, contactIndex, force);
                    observation.HandNormalForce += Math.Abs(force[0]);
                }

                observation.SourceSupport |= sourceGeoms.Contains(otherGeom);
                if (!targetGeoms.Contains(otherGeom))
                {
                    return;
                }

                observation.TargetSupport = true;
                if (scene.Model->geom_bodyid[itemGeom] == rootBodyIds[item])
                {
                    observation.LowerJawOnTarget = true;
                }
                else
                {
                    observation.UpperJawOnTarget = true;
                }
            }
        }

        /// <summary>Reset-to-rest is deliberately different from the legacy momentum-preserving teleport.</summary>
        public static void ResetProp(MjScene scene, MjFreeJoint joint, Vector3 position, Quaternion rotation, MjHingeJoint jaw, float jawDegrees)
        {
            MjEngineTool.SetMjTransform(scene.Data->qpos + joint.QposAddress, position, rotation);
            ClearDofs(joint.DofAddress, 6);
            ClearBodyWrench(joint.ParentBody.MujocoId);
            if (jaw != null)
            {
                scene.Data->qpos[jaw.QposAddress] = jawDegrees * Mathf.Deg2Rad;
                ClearDofs(jaw.DofAddress, 1);
                ClearBodyWrench(jaw.ParentBody.MujocoId);
            }

            void ClearBodyWrench(int bodyId)
            {
                for (int axis = 0; axis < 6; axis++)
                {
                    scene.Data->xfrc_applied[bodyId * 6 + axis] = 0;
                }
            }

            void ClearDofs(int address, int count)
            {
                for (int offset = 0; offset < count; offset++)
                {
                    scene.Data->qvel[address + offset] = 0;
                    scene.Data->qacc_warmstart[address + offset] = 0;
                    scene.Data->qfrc_applied[address + offset] = 0;
                }
            }
        }

        public static void Refresh(MjScene scene)
        {
            MujocoLib.mj_forward(scene.Model, scene.Data);
            scene.SyncUnityToMjState();
        }
    }
}
