using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GrabMaterials
{
    internal class ContainerFinder
    {
        // Method to find all containers within a given radius
        public static List<Container> FindNearbyContainers(Vector3 position, float radius)
        {
            Debug.Log($"FindNearbyContainers with {radius} meters");
            Debug.Log(position);
            List<Container> nearbyContainers = new List<Container>();
            Collider[] hitColliders = Physics.OverlapSphere(position, radius);
            Debug.Log($"{hitColliders.Count()} colliders");
            foreach (var hitCollider in hitColliders)
            {
                Debug.Log($"{hitCollider.name}");
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
