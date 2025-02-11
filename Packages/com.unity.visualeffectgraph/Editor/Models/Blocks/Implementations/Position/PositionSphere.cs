using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.VFX.Block
{
    class PositionSphereProvider : VariantProvider
    {
        public override IEnumerable<Variant> GetVariants()
        {
            yield return new Variant(
                VFXBlockUtility.GetNameString(AttributeCompositionMode.Overwrite) + " Position (Shape: Sphere)",
                "Position/Position on shape",
                typeof(PositionSphere),
                new[] {new KeyValuePair<string, object>("compositionPosition", AttributeCompositionMode.Overwrite)});
        }
    }

    [VFXHelpURL("Block-SetPosition(Sphere)")]
    [VFXInfo(variantProvider = typeof(PositionSphereProvider))]
    class PositionSphere : PositionBase
    {
        public override string name { get { return string.Format(base.name, "Arc Sphere"); } }

        public class InputProperties
        {
            [Tooltip("Sets the sphere used for positioning the particles.")]
            public TArcSphere arcSphere = TArcSphere.defaultValue;
        }

        public class CustomProperties
        {
            [Range(0, 1), Tooltip("When using customized emission, control the position around the arc to emit particles from.")]
            public float arcSequencer = 0.0f;
        }

        protected override bool needDirectionWrite => true;

        public override IEnumerable<VFXNamedExpression> parameters
        {
            get
            {
                var allSlots = GetExpressionsFromSlots(this);
                foreach (var p in allSlots.Where(e => e.name == "arcSphere_arc"
                    || e.name == "arcSequencer"))
                    yield return p;

                if (compositionPosition == AttributeCompositionMode.Blend)
                    yield return allSlots.FirstOrDefault(o => o.name == "blendPosition");
                if (compositionDirection == AttributeCompositionMode.Blend)
                    yield return allSlots.FirstOrDefault(o => o.name == "blendDirection");

                var transform = allSlots.FirstOrDefault(o => o.name == "arcSphere_sphere_transform").exp;
                var thickness = allSlots.FirstOrDefault(o => o.name == "Thickness").exp;
                var radius = allSlots.FirstOrDefault(o => o.name == "arcSphere_sphere_radius").exp;

                var radiusScale = VFXOperatorUtility.UniformScaleMatrix(radius);
                var finalTransform = new VFXExpressionTransformMatrix(transform, radiusScale);
                var inverseTransposeTRS =  VFXOperatorUtility.InverseTransposeTRS(transform);
                yield return new VFXNamedExpression(finalTransform, "transform");
                yield return new VFXNamedExpression(inverseTransposeTRS, "inverseTranspose");
                yield return new VFXNamedExpression(CalculateVolumeFactor(positionMode, radius, thickness), "volumeFactor");
            }
        }

        public override string source
        {
            get
            {
                var outSource = @"float cosPhi = 2.0f * RAND - 1.0f;";
                if (spawnMode == SpawnMode.Random)
                    outSource += @"float theta = arcSphere_arc * RAND;";
                else
                    outSource += @"float theta = arcSphere_arc * arcSequencer;";

                outSource += @"
float rNorm = pow(volumeFactor + (1 - volumeFactor) * RAND, 1.0f / 3.0f);
float2 sincosTheta;
sincos(theta, sincosTheta.x, sincosTheta.y);
sincosTheta *= sqrt(1.0f - cosPhi * cosPhi);
float3 finalDir = float3(sincosTheta, cosPhi);
float3 finalPos = float3(sincosTheta, cosPhi) * rNorm;
finalPos = mul(transform, float4(finalPos, 1.0f)).xyz;
finalDir = mul(inverseTranspose, float4(finalDir, 0.0f)).xyz;
finalDir = normalize(finalDir);";

                outSource += string.Format(composeDirectionFormatString, "finalDir") + "\n";
                outSource += string.Format(composePositionFormatString, "finalPos");

                return outSource;
            }
        }
    }
}
