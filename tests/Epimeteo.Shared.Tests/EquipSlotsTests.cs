using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class EquipSlotsTests
{
    [Theory]
    [InlineData(EquipCategory.MainHand, EquipSlot.MainHand)]
    [InlineData(EquipCategory.OffHand, EquipSlot.OffHand)]
    [InlineData(EquipCategory.Head, EquipSlot.Head)]
    [InlineData(EquipCategory.Chest, EquipSlot.Chest)]
    [InlineData(EquipCategory.Hands, EquipSlot.Hands)]
    [InlineData(EquipCategory.Legs, EquipSlot.Legs)]
    [InlineData(EquipCategory.Feet, EquipSlot.Feet)]
    [InlineData(EquipCategory.Cloak, EquipSlot.Cloak)]
    [InlineData(EquipCategory.Amulet, EquipSlot.Amulet)]
    [InlineData(EquipCategory.Tool, EquipSlot.Tool)]
    public void CategoriaNormal_ResuelveAUnSoloSlot(EquipCategory category, EquipSlot expected)
    {
        var slots = EquipSlots.Resolve(category);

        Assert.Single(slots);
        Assert.Equal(expected, slots[0]);
        Assert.True(EquipSlots.IsValid(category, expected));
    }

    [Fact]
    public void Ring_ResuelveALosDosHuecosDeAnillo()
    {
        var slots = EquipSlots.Resolve(EquipCategory.Ring);

        Assert.Equal(2, slots.Count);
        Assert.Contains(EquipSlot.Ring1, slots);
        Assert.Contains(EquipSlot.Ring2, slots);
        Assert.True(EquipSlots.IsValid(EquipCategory.Ring, EquipSlot.Ring1));
        Assert.True(EquipSlots.IsValid(EquipCategory.Ring, EquipSlot.Ring2));
    }

    [Fact]
    public void IsValid_ConSlotQueNoLeToca_DevuelveFalse()
    {
        Assert.False(EquipSlots.IsValid(EquipCategory.Head, EquipSlot.Chest));
        Assert.False(EquipSlots.IsValid(EquipCategory.Ring, EquipSlot.Head));
    }
}
