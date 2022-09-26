﻿#nullable enable
using Google.Protobuf.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf.Reflection.Internal.CodeGen.Collections;
using ProtoBuf.Internal.CodeGen;

namespace ProtoBuf.Reflection.Internal.CodeGen;

internal class CodeGenMessage : CodeGenType
{
    internal CodeGenMessage(string name, string fullyQualifiedPrefix) : base(name, fullyQualifiedPrefix)
    {
        OriginalName = base.Name;
    }

    private NonNullableList<CodeGenMessage>? _messages;
    private NonNullableList<CodeGenEnum>? _enums;
    private NonNullableList<CodeGenField>? _fields;
    public ICollection<CodeGenMessage> Messages => _messages ??= new();
    public ICollection<CodeGenEnum> Enums => _enums ??= new();
    public ICollection<CodeGenField> Fields => _fields ??= new();

    public bool ShouldSerializeMessages() => _messages is { Count: > 0 };
    public bool ShouldSerializeEnums() => _enums is { Count: > 0 };
    public bool ShouldSerializeFields() => _fields is { Count: > 0 };

    public new string Name
    {   // make setter public
        get => base.Name;
        set => base.Name = value;
    }
    public string OriginalName { get; set; }
    public string Package { get; set; } = "";

    [DefaultValue(false)]
    public bool IsDeprecated { get; set; }

    [DefaultValue(false)]
    public bool IsValueType { get; set; }

    [DefaultValue(Access.Public)]
    public Access Access { get; set; } = Access.Public;

    public bool ShouldSerializeOriginalName() => OriginalName != Name;
    public bool ShouldSerializePackage() => !string.IsNullOrWhiteSpace(Package);


    internal static CodeGenMessage Parse(DescriptorProto message, string fullyQualifiedPrefix, CodeGenParseContext context, string package)
    {
        var name = context.NameNormalizer.GetName(message);
        var newMessage = new CodeGenMessage(name, fullyQualifiedPrefix);
        context.Register(message.FullyQualifiedName, newMessage);
        newMessage.OriginalName = message.Name;
        newMessage.Package = package;

        if (message.Fields.Count > 0)
        {
            foreach (var field in message.Fields)
            {
                newMessage.Fields.Add(CodeGenField.Parse(field, context));
            }
        }

        if (message.NestedTypes.Count > 0 || message.EnumTypes.Count > 0)
        {
            var prefix = newMessage.FullyQualifiedPrefix + newMessage.Name + "+";
            foreach (var type in message.NestedTypes)
            {
                if (!context.AddMapEntry(type))
                {
                    newMessage.Messages.Add(CodeGenMessage.Parse(type, prefix, context, package));
                }
            }
            foreach (var type in message.EnumTypes)
            {
                newMessage.Enums.Add(CodeGenEnum.Parse(type, prefix, context, package));
            }
        }

        return newMessage;
    }

    internal void FixupPlaceholders(CodeGenParseContext context)
    {
        if (ShouldSerializeFields())
        {
            int nextTrackingIndex = 0;
            foreach (var field in Fields)
            {
                if (context.FixupPlaceholder(field.Type, out var found))
                {
                    field.Type = found;
                }
                if (field.Conditional == ConditionalKind.FieldPresence)
                {
                    if (field.Type is CodeGenMessage msg && !msg.IsValueType)
                    {
                        field.Conditional = ConditionalKind.Always; // uses null for tracking
                    }
                    else
                    {
                        field.FieldPresenceIndex = nextTrackingIndex++;
                    }
                }
                if (field.IsRepeated && field.Type is CodeGenMapEntryType)
                {
                    field.Repeated = RepeatedKind.Dictionary;
                }
            }
        }
    }
}
