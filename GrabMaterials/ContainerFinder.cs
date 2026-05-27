using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static GrabMaterialsMod.GrabMaterialsMod;

namespace GrabMaterials
{
    internal class ContainerFinder
    {
        // Method to find all containers within a given radius
        public static List<Container> FindNearbyContainers(Vector3 position, float radius)
        {
            Log.LogInfo($"FindNearbyContainers with {radius} meters");
            Log.LogInfo(position);
            List<Container> nearbyContainers = new List<Container>();
            Collider[] hitColliders = Physics.OverlapSphere(position, radius);
            Log.LogInfo($"{hitColliders.Count()} colliders");
            foreach (var hitCollider in hitColliders)
            {
                Log.LogInfo($"{hitCollider.name}");
                Container container = hitCollider.GetComponent<Container>();
                ContainerFilterService filter = container.GetComponent<ContainerFilterService>();
                
                if (container != null)
                {
                    nearbyContainers.Add(container);
                }
            }

            return nearbyContainers;
        }
    }
}
