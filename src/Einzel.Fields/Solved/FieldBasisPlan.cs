using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>
/// Which electrodes of each solved element get DC channels of their own: a plan a caller
/// decides once for many fields, so that a potential that changes between them changes a
/// weight rather than a pattern.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default decomposition puts every DC potential into one constant supply</b>, which
/// is the fewest solves for one field and a new solve for every field whose DC electrodes move
/// apart: a DC scan of a driven quadrupole solves its DC pattern at every point. Under a plan,
/// a group's electrodes leave that supply and form one of their own; the electrodes in no
/// group keep the constant supply (a static element: one channel holding them at their
/// potentials and every grouped electrode at zero, weighted one). A field is then the fixed
/// channel, one channel per group at one volt weighted by the group's potential, and the drive
/// channels as before.
/// </para>
/// <para>
/// <b>Superposition, not the same bits.</b> A sum of channels matches a direct solve to the
/// solver's convergence (a few microvolts on a 1500 V quadrupole), so a field built under a
/// plan is as accurate as the default one and not bitwise equal to it. Which plan applies is
/// the caller's to decide from what the fields are for, never from what a cache holds.
/// </para>
/// <para>
/// <b>Groups are disjoint, and every name is an electrode.</b> The constructor refuses an
/// electrode named twice within one element - in two groups, or twice in one - an empty
/// group and a negative element index: a static element would count such an electrode once
/// per group. The builders and <see cref="SolveHash"/> refuse, when the plan is used, an
/// element the model does not solve and a name the element does not declare.
/// </para>
/// <para>
/// <b>What a group becomes depends on the element.</b> A static element under a plan is a
/// <see cref="WeightedSolvedField"/>, time-free like the single channel it replaces, so the
/// integrator treats it exactly as it treats a static field; a group is a unit channel there
/// only while its electrodes hold one potential, and one whose electrodes differ stays in the
/// fixed channel at their potentials. A driven element is a <see cref="DrivenSolvedField"/>
/// with one more channel per group, and a group whose electrodes differ is a channel normalised
/// to its leading electrode, whose pattern changes whenever their ratio does. A driven group at
/// 0 V feeds no supply and has no channel, so two models share their keys when a group holds
/// two potentials that are not zero, not when one of them is; a static element keeps the unit
/// channel at weight zero. An edge profile, whose volts are its profile, is never grouped. An
/// element that steps through stages keeps the default decomposition: a plan is about the DC of
/// one operating point, and a sequence weighs each stage against its own patterns.
/// </para>
/// </remarks>
public sealed record FieldBasisPlan
{
    /// <summary>Makes a plan, refusing one whose groups overlap.</summary>
    /// <param name="groups">
    /// Field element index to its DC groups, each a list of electrode names. An element not named,
    /// or named with no groups, keeps the default decomposition.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="groups"/>, a group list, a group or a name is null.</exception>
    /// <exception cref="ArgumentException">
    /// An element index is negative, a group is empty, or an electrode is named twice within one element.
    /// </exception>
    public FieldBasisPlan(IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<string>>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        foreach (var (element, list) in groups)
        {
            if (element < 0)
            {
                throw new ArgumentException($"field element {element} is not an element index", nameof(groups));
            }

            ArgumentNullException.ThrowIfNull(list, nameof(groups));
            var named = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in list)
            {
                ArgumentNullException.ThrowIfNull(group, nameof(groups));

                if (group.Count == 0)
                {
                    throw new ArgumentException($"field element {element} has an empty group", nameof(groups));
                }

                foreach (var name in group)
                {
                    ArgumentNullException.ThrowIfNull(name, nameof(groups));

                    if (!named.Add(name))
                    {
                        throw new ArgumentException(
                            $"electrode '{name}' of field element {element} is named twice; a plan's groups are disjoint",
                            nameof(groups));
                    }
                }
            }
        }

        Groups = groups;
    }

    /// <summary>Field element index to its DC groups, each a list of electrode names.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<string>>> Groups { get; }

    /// <summary>One element's groups, or null when the element keeps the default decomposition.</summary>
    /// <param name="element">The field element index.</param>
    /// <returns>The groups.</returns>
    public IReadOnlyList<IReadOnlyList<string>>? GroupsOf(int element) =>
        Groups.TryGetValue(element, out var groups) && groups.Count > 0 ? groups : null;

    /// <summary>Refuses a plan that names an element the model does not solve.</summary>
    /// <param name="model">The model the plan is used on.</param>
    /// <exception cref="ArgumentException">The plan names a field element that is not a solved element of the model.</exception>
    internal void CheckElements(CompiledModel model)
    {
        foreach (var element in Groups.Keys)
        {
            if (GroupsOf(element) is not null
                && (element >= model.Fields.Count || model.Fields[element] is { Solve: null, Solve3D: null }))
            {
                throw new ArgumentException(
                    $"the plan names field element {element}, which is not a solved element of this model");
            }
        }
    }

    /// <summary>Refuses groups that name an electrode the element does not declare.</summary>
    /// <param name="groups">The element's groups, or null.</param>
    /// <param name="declared">The element's electrode names.</param>
    /// <exception cref="ArgumentException">A name is not one of the element's electrodes.</exception>
    internal static void CheckNames(IReadOnlyList<IReadOnlyList<string>>? groups, IReadOnlyCollection<string> declared)
    {
        foreach (var name in groups?.SelectMany(group => group) ?? [])
        {
            if (!declared.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"the plan names '{name}', which this element does not declare (its electrodes: {string.Join(", ", declared)})");
            }
        }
    }

    /// <summary>Electrode name to group index, as the decomposition takes it.</summary>
    /// <param name="groups">The groups, or null.</param>
    /// <returns>The map, or null when there are no groups.</returns>
    internal static Dictionary<string, int>? Map(IReadOnlyList<IReadOnlyList<string>>? groups)
    {
        if (groups is null)
        {
            return null;
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var group = 0; group < groups.Count; group++)
        {
            foreach (var name in groups[group])
            {
                map[name] = group;
            }
        }

        return map;
    }
}
