using FluentAssertions;
using ServiceHub.Core.Helpers;

namespace ServiceHub.UnitTests.Helpers;

/// <summary>Design 10 §5: a queue may split into a bounded number of shapes; later new shapes fold into one "other".</summary>
public sealed class ErrorTemplateCapTests
{
    private static List<string> Shapes(int n) => Enumerable.Range(1, n).Select(i => $"shape {i}").ToList();

    [Fact]
    public void A_known_shape_keeps_its_own_signature_even_when_the_queue_is_full()
        => ErrorTemplateCap.Apply(Shapes(20), "shape 7", 20).Should().Be("shape 7");

    [Fact]
    public void A_new_shape_takes_a_free_slot()
        => ErrorTemplateCap.Apply(Shapes(19), "brand new", 20).Should().Be("brand new");

    [Fact]
    public void A_new_shape_on_a_full_queue_folds_into_other()
        => ErrorTemplateCap.Apply(Shapes(20), "brand new", 20).Should().Be(ErrorTemplateCap.Other);

    [Fact]
    public void An_empty_shape_is_its_own_group_and_uses_no_slot()
    {
        ErrorTemplateCap.Apply(Shapes(20), "", 20).Should().Be("");
        ErrorTemplateCap.Apply([], "", 20).Should().Be("");
    }

    [Fact]
    public void Empty_and_other_entries_in_the_known_list_do_not_use_slots()
    {
        var known = Shapes(19);
        known.Add("");
        known.Add(ErrorTemplateCap.Other);
        ErrorTemplateCap.Apply(known, "brand new", 20).Should().Be("brand new");
    }

    [Fact]
    public void Other_stays_other()
        => ErrorTemplateCap.Apply(Shapes(3), ErrorTemplateCap.Other, 20).Should().Be(ErrorTemplateCap.Other);

    [Fact]
    public void The_limit_must_be_at_least_one()
    {
        var act = () => ErrorTemplateCap.Apply([], "x", 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_default_is_twenty()
        => ErrorTemplateCap.DefaultMax.Should().Be(20);

    [Fact]
    public void AssignAll_gives_each_message_what_Apply_would_have_given_it_on_arrival()
    {
        var arrivals = new[] { "a", "b", "", "a", "c", "d", "b", "e", "d" };
        var expected = new List<string>();
        var known = new List<string>();
        foreach (var t in arrivals)
        {
            var capped = ErrorTemplateCap.Apply(known, t, 3);
            expected.Add(capped);
            if (capped.Length > 0 && capped != ErrorTemplateCap.Other && !known.Contains(capped))
            {
                known.Add(capped);
            }
        }

        ErrorTemplateCap.AssignAll(arrivals, 3).Should().Equal(expected);
        expected.Should().Equal("a", "b", "", "a", "c", ErrorTemplateCap.Other, "b", ErrorTemplateCap.Other, ErrorTemplateCap.Other);
    }
}
