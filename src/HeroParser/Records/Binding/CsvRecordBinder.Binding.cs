using HeroParser.SeparatedValues;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HeroParser;

internal sealed partial class CsvRecordBinder<T> where T : class, new()
{
    private sealed class MemberBinding
    {
        public MemberBinding(
            string memberName,
            Type targetType,
            string headerName,
            int? attributeIndex,
            ColumnConverter converter,
            Action<T, object?> setter)
        {
            MemberName = memberName;
            TargetType = targetType;
            HeaderName = headerName;
            AttributeIndex = attributeIndex;
            Converter = converter;
            Setter = setter;
        }

        public string MemberName { get; }
        public Type TargetType { get; }
        public string HeaderName { get; }
        public int? AttributeIndex { get; }
        public int? ResolvedIndex { get; set; }
        private ColumnConverter Converter { get; }
        private Action<T, object?> Setter { get; }

        public bool TryAssign(T instance, CsvCharSpanColumn column)
        {
            if (!Converter(column, out var value))
                return false;

            Setter(instance, value);
            return true;
        }
    }

    internal sealed record BindingTemplate(
        string MemberName,
        Type TargetType,
        string HeaderName,
        int? AttributeIndex,
        ColumnConverter Converter,
        Action<T, object?> Setter);

    private static readonly ConcurrentDictionary<Type, List<BindingTemplate>> bindingCache = new();

    internal delegate bool ColumnConverter(CsvCharSpanColumn column, out object? value);
}
