using Lumina.Models;
using Lumina.Services.Folders;

namespace Lumina.Tests;

public class FolderServiceHelperTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static Folder MakeFolder(Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        ParentId = parentId,
        Name = id.ToString()[..8],
        CreatedAt = DateTime.UtcNow
    };

    private static Dictionary<Guid, Folder> Dict(params Folder[] folders) =>
        folders.ToDictionary(f => f.Id);

    // ── NormalizeName ────────────────────────────────────────────────────────

    [Fact]
    public void NormalizeName_TrimsWhitespace()
    {
        Assert.Equal("hello", FolderService.NormalizeName("  hello  "));
    }

    [Fact]
    public void NormalizeName_EmptyAfterTrim_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => FolderService.NormalizeName("   "));
    }

    [Fact]
    public void NormalizeName_TooLong_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FolderService.NormalizeName(new string('a', 101)));
    }

    [Fact]
    public void NormalizeName_ExactlyMaxLength_IsAllowed()
    {
        var name = new string('a', 100);
        Assert.Equal(name, FolderService.NormalizeName(name));
    }

    // ── EnsureNameAvailable ──────────────────────────────────────────────────

    [Fact]
    public void EnsureNameAvailable_NoClash_DoesNotThrow()
    {
        var a = MakeFolder(Guid.NewGuid());
        FolderService.EnsureNameAvailable([a], null, "other-name", excludeId: null);
    }

    [Fact]
    public void EnsureNameAvailable_SameNameSameParent_Throws()
    {
        var parent = Guid.NewGuid();
        var a = MakeFolder(Guid.NewGuid(), parentId: parent);
        a.Name = "Docs";

        var ex = Assert.Throws<InvalidOperationException>(
            () => FolderService.EnsureNameAvailable([a], parent, "Docs", excludeId: null));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void EnsureNameAvailable_CaseInsensitiveClash_Throws()
    {
        var parent = Guid.NewGuid();
        var a = MakeFolder(Guid.NewGuid(), parentId: parent);
        a.Name = "docs";

        Assert.Throws<InvalidOperationException>(
            () => FolderService.EnsureNameAvailable([a], parent, "DOCS", excludeId: null));
    }

    [Fact]
    public void EnsureNameAvailable_SameNameDifferentParent_DoesNotThrow()
    {
        var a = MakeFolder(Guid.NewGuid(), parentId: Guid.NewGuid());
        a.Name = "Docs";

        // Inserting under a different parent — should be fine.
        FolderService.EnsureNameAvailable([a], Guid.NewGuid(), "Docs", excludeId: null);
    }

    [Fact]
    public void EnsureNameAvailable_ExcludedId_DoesNotThrow()
    {
        // Self-rename: the folder itself has the same name — must not count as a clash.
        var parent = Guid.NewGuid();
        var id = Guid.NewGuid();
        var a = MakeFolder(id, parentId: parent);
        a.Name = "Docs";

        FolderService.EnsureNameAvailable([a], parent, "Docs", excludeId: id);
    }

    // ── DepthOf ──────────────────────────────────────────────────────────────

    [Fact]
    public void DepthOf_RootFolder_ReturnsOne()
    {
        var root = MakeFolder(Guid.NewGuid());
        Assert.Equal(1, FolderService.DepthOf(root, Dict(root)));
    }

    [Fact]
    public void DepthOf_OneLevel_ReturnsTwo()
    {
        var root = MakeFolder(Guid.NewGuid());
        var child = MakeFolder(Guid.NewGuid(), parentId: root.Id);

        Assert.Equal(2, FolderService.DepthOf(child, Dict(root, child)));
    }

    [Fact]
    public void DepthOf_TwoLevels_ReturnsThree()
    {
        var root = MakeFolder(Guid.NewGuid());
        var child = MakeFolder(Guid.NewGuid(), parentId: root.Id);
        var grandchild = MakeFolder(Guid.NewGuid(), parentId: child.Id);

        Assert.Equal(3, FolderService.DepthOf(grandchild, Dict(root, child, grandchild)));
    }

    // ── SubtreeHeight ────────────────────────────────────────────────────────

    [Fact]
    public void SubtreeHeight_Leaf_ReturnsOne()
    {
        var leaf = MakeFolder(Guid.NewGuid());
        Assert.Equal(1, FolderService.SubtreeHeight(leaf.Id, Dict(leaf)));
    }

    [Fact]
    public void SubtreeHeight_OneChild_ReturnsTwo()
    {
        var root = MakeFolder(Guid.NewGuid());
        var child = MakeFolder(Guid.NewGuid(), parentId: root.Id);

        Assert.Equal(2, FolderService.SubtreeHeight(root.Id, Dict(root, child)));
    }

    [Fact]
    public void SubtreeHeight_TakesDeepestBranch()
    {
        // root → childA → grandchild  (height 3)
        // root → childB               (height 2)
        var root = MakeFolder(Guid.NewGuid());
        var childA = MakeFolder(Guid.NewGuid(), parentId: root.Id);
        var childB = MakeFolder(Guid.NewGuid(), parentId: root.Id);
        var grandchild = MakeFolder(Guid.NewGuid(), parentId: childA.Id);

        Assert.Equal(3, FolderService.SubtreeHeight(root.Id, Dict(root, childA, childB, grandchild)));
    }

    // ── IsDescendant ─────────────────────────────────────────────────────────

    [Fact]
    public void IsDescendant_DirectChild_ReturnsTrue()
    {
        var root = MakeFolder(Guid.NewGuid());
        var child = MakeFolder(Guid.NewGuid(), parentId: root.Id);

        Assert.True(FolderService.IsDescendant(child.Id, root.Id, Dict(root, child)));
    }

    [Fact]
    public void IsDescendant_Grandchild_ReturnsTrue()
    {
        var root = MakeFolder(Guid.NewGuid());
        var child = MakeFolder(Guid.NewGuid(), parentId: root.Id);
        var grandchild = MakeFolder(Guid.NewGuid(), parentId: child.Id);

        Assert.True(FolderService.IsDescendant(grandchild.Id, root.Id, Dict(root, child, grandchild)));
    }

    [Fact]
    public void IsDescendant_UnrelatedFolder_ReturnsFalse()
    {
        var a = MakeFolder(Guid.NewGuid());
        var b = MakeFolder(Guid.NewGuid());

        Assert.False(FolderService.IsDescendant(b.Id, a.Id, Dict(a, b)));
    }

    [Fact]
    public void IsDescendant_RootFolder_ReturnsFalse()
    {
        // A root has no parent, so it can't be a descendant of anything.
        var root = MakeFolder(Guid.NewGuid());
        var other = MakeFolder(Guid.NewGuid());

        Assert.False(FolderService.IsDescendant(root.Id, other.Id, Dict(root, other)));
    }

    [Fact]
    public void IsDescendant_SiblingIsNotDescendantOfParent()
    {
        // This verifies that sibling A is not considered inside sibling B's subtree.
        var parent = MakeFolder(Guid.NewGuid());
        var sibA = MakeFolder(Guid.NewGuid(), parentId: parent.Id);
        var sibB = MakeFolder(Guid.NewGuid(), parentId: parent.Id);

        Assert.False(FolderService.IsDescendant(sibA.Id, sibB.Id, Dict(parent, sibA, sibB)));
    }
}
