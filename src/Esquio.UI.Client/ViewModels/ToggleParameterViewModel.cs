﻿namespace Esquio.UI.Client.ViewModels
{
    public class ToggleParameterViewModel
    {
        public ToggleParameterViewModel(
            string name,
            string clrType,
            string value = default,
            string ringName = default,
            int order = default,
            string description = default)
        {
            Name = name;
            ClrType = clrType;
            Value = value;
            RingName = ringName;
            Order = order;
            Description = description;
        }


        public string Name { get; set; }

        public string ClrType { get; set; }

        public string Value { get; set; }

        public string RingName { get; set; }

        public int Order { get; set; }

        public string Description { get; set; }

        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }
}