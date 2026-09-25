using UnityEngine;

namespace Coat
{
    /// ConfigurableJoint.targetRotation is expressed in the joint's own axis space and
    /// is measured backwards from the pose the joint was created in, which makes it
    /// unusable directly. This converts an ordinary "I want this local rotation" into
    /// what the joint actually wants.
    public static class ConfigurableJointExtensions
    {
        public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            Quaternion result = Quaternion.Inverse(worldToJointSpace);
            result *= Quaternion.Inverse(targetLocalRotation) * startLocalRotation;
            result *= worldToJointSpace;

            joint.targetRotation = result;
        }
    }
}
