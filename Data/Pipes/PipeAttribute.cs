using System;

namespace SAApi.Data.Pipes
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PipeAttribute : Attribute
    {
        public string Directive { get; private set; }
        public Type Options { get; private set; }

        public PipeAttribute(string directive, Type options = null)
        {
            Directive = directive;
            Options = options;
        }
    }
}