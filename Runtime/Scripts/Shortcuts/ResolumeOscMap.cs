using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resolink
{
    /// <summary>
    /// Stores data about an OSC event configuration from Resolume
    /// </summary>
    [Serializable]
    public class ResolumeOscMap : ScriptableObject
    {
        /// <summary>
        /// The version of Resolume that created the source file 
        /// </summary>
        public ResolumeVersion Version;
        
        /// <summary>
        /// All active 'shortcuts' (events) in the configuration
        /// </summary>
        public List<ResolumeOscShortcut> Shortcuts = new List<ResolumeOscShortcut>();

        /// <summary>
        /// All shortcuts that make up r/g/b/a components of a color, grouped together
        /// </summary>
        public List<ColorShortcutGroup> ColorGroups = new List<ColorShortcutGroup>();
    }
}