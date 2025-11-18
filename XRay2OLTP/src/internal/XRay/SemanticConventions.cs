using System;
using System.Collections.Generic;
using System.Text;

namespace XRay
{
    internal static class SemanticConventions
    {
        public const string InstrumentationLibraryName = "XRay";

        public const string ScopeXRay = "xray";
        public const string ScopeProperties = "prop";

        public const string DependencyType = ScopeXRay+".dependencytype";
        public const string Name = ScopeXRay + ".name";
        public const string Url = ScopeXRay + ".url";
        public const string Status = ScopeXRay + ".status";
        public const string Data = ScopeXRay + ".data";
        public const string ResultCode = ScopeXRay + ".resultcode";

        

    }
}
