using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace OnTopReplica.Platforms {
    class WindowsXp : PlatformSupport {
        
        public override bool CheckCompatibility() {
            MessageBox.Show("No DWM on this platform", "Error no DWM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

    }
}
