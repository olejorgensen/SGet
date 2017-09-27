using System;

namespace SGet
{
    public class PropertyModel
    {
        public PropertyModel(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; } = String.Empty;

        public string Value { get; } = String.Empty;
    }
}
