using System;

namespace SAApi.Data.Pipes
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PipeAttribute : Attribute
    {
        public string Directive { get; private set; }

        public PipeAttribute(string directive)
        {
            Directive = directive;
        }
    }
}