using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Plugin.Phd2Tools.Dockables {

    [Export(typeof(ResourceDictionary))]
    public partial class Phd2ToolsPanelTemplate : ResourceDictionary {

        public Phd2ToolsPanelTemplate() {
            InitializeComponent();
        }
    }
}