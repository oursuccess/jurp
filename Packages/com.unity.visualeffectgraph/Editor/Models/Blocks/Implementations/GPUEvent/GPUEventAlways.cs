using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.VFX.Block
{
    [VFXHelpURL("Block-TriggerEventAlways")]
    [VFXInfo(name = "Trigger Event Always", category = "GPUEvent", experimental = true)]
    class GPUEventAlways : VFXBlock
    {
        public override string name { get { return "Trigger Event Always"; } }
        public override VFXContextType compatibleContexts { get { return VFXContextType.Update; } }
        public override VFXDataType compatibleData { get { return VFXDataType.Particle; } }

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                yield return new VFXAttributeInfo(VFXAttribute.EventCount, VFXAttributeMode.Write);
            }
        }

        public class InputProperties
        {
            [Tooltip("Sets the number of particles spawned via a GPU event when this block is triggered.")]
            public uint count = 1u;
        }

        public class OutputProperties
        {
            [Tooltip("Outputs a GPU event which can connect to another system via a GPUEvent context. Attributes from the current system can be inherited in the new system.")]
            public GPUEvent evt = new GPUEvent();
        }

        public override string source
        {
            get
            {
                return "eventCount = count;";
            }
        }
    }
}
