using NUnit.Framework;
using GrabMaterialsMod;
using UnityEngine;

namespace GrabMaterialsModTests
{
	[TestFixture]
	public class Tests
	{
		private GrabMaterialsMod.GrabMaterialsMod _mod;

		[SetUp]
		public void Setup()
		{
			_mod = new GrabMaterialsMod.GrabMaterialsMod();
		}

		[Test]
		public void TestGrabItemsFromNearbyContainers()
		{
			// Arrange
			//var args = new Terminal.ConsoleEventArgs(new List<string> { "grab", "wood", "10" });

			var container = new Container();
			var inventory = container.GetInventory();
			inventory.AddItem("wood", 4, 1, 1, 1, "dude");
			inventory.AddItem("wood", 5, 1, 1, 1, "dude");
			inventory.AddItem("wood", 2, 1, 1, 1, "dude");
			inventory.AddItem("wood", 3, 1, 1, 1, "dude");

			Assert.That(inventory.CountItems("wood"), Is.EqualTo(4 + 5 + 2 + 3)); //
			//inventory.CountItems

			Player.m_localPlayer = new Player();

			// Act
			//GrabMaterialsMod.GrabItemsFromNearbyContainers(args);
			GrabMaterialsMod.GrabMaterialsMod.GrabItemFromContainer(container, "wood", 10);
			Assert.That(inventory.CountItems("wood"), Is.EqualTo(4 + 5 + 2 + 3 - 10)); //

			// Assert
			// Add assertions to verify the expected behavior
		}

		//[Test]
		//public void TestCountItemsInInventory()
		//{
		//	// Arrange
		//	var inventory = new Inventory("TestInventory", null, 10, 4);

		//	// Act
		//	var count = GrabMaterialsMod.CountItemsInInventory(inventory);

		//	// Assert
		//	Assert.AreEqual(0, count);
		//}

		//[Test]
		//public void TestCountItemsInInventory_WithName()
		//{
		//	// Arrange
		//	var inventory = new Inventory("TestInventory", null, 10, 4);
		//	var item = new ItemDrop.ItemData { m_shared = new ItemDrop.ItemData.SharedData { m_name = "wood" }, m_stack = 5 };
		//	inventory.AddItem(item);

		//	// Act
		//	var count = GrabMaterialsMod.CountItemsInInventory(inventory, "wood");

		//	// Assert
		//	Assert.AreEqual(5, count);
		//}
	}
}

