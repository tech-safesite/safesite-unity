using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Modules.SceneRig.Editor
{
    /// <summary>What a scene is for, which decides the rig it should have.</summary>
    public enum SceneRigKind
    {
        /// <summary>Bootstrap: owns the one player (--Player--); scenarios load into it.</summary>
        AppShell,

        /// <summary>A scenario scene: loads additively into Bootstrap, so it has no rig of its own.</summary>
        Scenario,

        /// <summary>Any other project scene, played on its own: needs the project rig.</summary>
        Standalone,

        /// <summary>Package and sample scenes: not checked.</summary>
        Ignored,
    }

    public enum RigIssueSeverity
    {
        Error,
        Warning,
        Info,
    }

    public sealed class RigIssue
    {
        public RigIssueSeverity Severity;
        public string Message;

        /// <summary>The object the issue is about, for selecting it; may be null.</summary>
        public Object Context;

        public string FixLabel;
        public Action Fix;

        public bool CanFix => Fix != null;
    }

    public sealed class SceneRigReport
    {
        public Scene Scene;
        public SceneRigKind Kind;
        public readonly List<RigIssue> Issues = new List<RigIssue>();

        public bool Ok => Issues.All(i => i.Severity != RigIssueSeverity.Error);
        public int ErrorCount => Issues.Count(i => i.Severity == RigIssueSeverity.Error);
        public int WarningCount => Issues.Count(i => i.Severity == RigIssueSeverity.Warning);
        public int FixableCount => Issues.Count(i => i.CanFix);

        internal void Add(RigIssueSeverity severity, string message, Object context = null, string fixLabel = null, Action fix = null)
        {
            Issues.Add(new RigIssue { Severity = severity, Message = message, Context = context, FixLabel = fixLabel, Fix = fix });
        }
    }
}
