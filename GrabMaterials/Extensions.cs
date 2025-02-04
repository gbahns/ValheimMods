using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrabMaterials
{
	internal static class Extensions
	{
		public static string Name(this ItemDrop.ItemData self)
		{
			return self.m_shared.m_name.Substring(6);
		}

		public static int Count(this ItemDrop.ItemData self)
		{
			return self.m_stack;
		}
	}
}
