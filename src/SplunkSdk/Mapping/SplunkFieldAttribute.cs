namespace Marouanvs.Splunk.Mapping;

/// <summary>
/// Maps a Splunk result field to a DTO property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SplunkFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a field mapping attribute.
    /// </summary>
    /// <param name="name">Splunk result field name.</param>
    public SplunkFieldAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A Splunk field name is required.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the Splunk result field name.
    /// </summary>
    public string Name { get; }
}
