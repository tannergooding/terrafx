// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputActionMapValidatorTests
{
    private static InputActionMapV1 CreateMap(
        ImmutableArray<ActionDefinition> actions,
        ImmutableArray<ContextDefinition> contexts,
        ImmutableArray<BindingDefinition> bindings,
        Guid? mapId = null)
        => new InputActionMapV1(
            1,
            mapId ?? Guid.NewGuid(),
            1,
            contexts.IsDefaultOrEmpty ? 0 : 1UL << contexts[0].Bit,
            actions,
            contexts,
            bindings);

    [Test]
    public void AcceptsUtf8ByteBoundariesForActionAndLogicalKeyNames()
    {
        var action = new ActionDefinition(Guid.NewGuid(), new string('\u00E9', 64), InputActionKind.Digital);
        var control = new DigitalControl(new KeyboardControl(new string('\u00E9', 32)));
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = control,
        };
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [binding]);

        Assert.That(InputActionMapValidator.Validate(map), Is.Null);
    }

    [Test]
    public void RejectsDuplicateControlsInChord()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Chord", InputActionKind.Digital);
        var control = new DigitalControl(new KeyboardControl(7, 80));
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Chord) {
            Controls = [control, new DigitalControl(new KeyboardControl(7, 80))],
        };
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [binding]);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("DuplicateControl"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Bindings/0/Controls/1"));
    }

    [Test]
    public void RejectsExtraneousKindSpecificFields()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Jump", InputActionKind.Digital);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(new KeyboardControl(7, 80)),
            Negative = new KeyboardControl(7, 81),
        };
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [binding]);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidSchema"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Bindings/0/Negative"));
    }

    [Test]
    public void RejectsInvalidAxisProcessorAndUsesStableSchemaPath()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.GamepadAxis1) {
            Axis1Source = new GamepadAxis1Source(0, GamepadAxis.LeftX, deadzone: float.NaN),
        };
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [binding]);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidProcessor"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Bindings/0/Axis1Source/Deadzone"));
    }

    [Test]
    public void RejectsOverLimitAndIllFormedUtf8Names()
    {
        var action = new ActionDefinition(Guid.NewGuid(), new string('\u00E9', 65), InputActionKind.Digital);
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            []);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidStringOrEncoding"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Actions/0/Name"));

        var illFormed = new ActionDefinition(Guid.NewGuid(), "\uD800", InputActionKind.Digital);
        map = CreateMap(
            [illFormed],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            []);

        issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidStringOrEncoding"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Actions/0/Name"));
    }

    [Test]
    public void RejectsUninitializedCollections()
    {
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            0,
            default,
            default,
            default);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidSchema"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Actions"));
    }

    [Test]
    public void SelectsChordWindowIssueBeforeControlArrayIssue()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Chord", InputActionKind.Digital);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Chord) {
            CompletionWindowMilliseconds = 0,
            Controls = [],
        };
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [binding]);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidProcessor"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Bindings/0/CompletionWindowMilliseconds"));
    }

    [Test]
    public void SelectsTheFirstIssueByOrdinalSchemaPath()
    {
        var action = new ActionDefinition(Guid.Empty, string.Empty, InputActionKind.Digital);
        var map = CreateMap(
            [action],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            [],
            mapId: Guid.Empty);

        var issue = InputActionMapValidator.Validate(map);

        Assert.That(issue?.Code, Is.EqualTo("InvalidSchema"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/Actions/0/ActionId"));
    }
}
