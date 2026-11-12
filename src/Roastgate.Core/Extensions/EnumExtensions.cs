using System.ComponentModel;
using System.Reflection;

namespace Roastgate.Core.Extensions;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        Type type = value.GetType();

        string name = value.ToString();

        FieldInfo? field = type.GetField(name);

        if (field == null)
        {
            return name;
        }

        if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
        {
            return attr.Description;
        }

        return name;
    }

    public static T? FromDescription<T>(this string description) where T : struct, Enum
    {
        foreach (FieldInfo field in typeof(T).GetFields())
        {
            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            {
                if (attribute.Description == description)
                {
                    return (T?)field.GetValue(null);
                }
            }
            else
            {
                if (field.Name == description)
                {
                    return (T?)field.GetValue(null);
                }
            }
        }

        return null;
    }
}
