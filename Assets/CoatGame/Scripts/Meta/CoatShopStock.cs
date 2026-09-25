using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Everything the shop sells, as an asset.
    ///
    /// Lives at Assets/CoatGame/Resources/CoatShopStock.asset so CoatCatalogue
    /// can find it by name at runtime with no scene reference to wire up.
    [CreateAssetMenu(fileName = "CoatShopStock", menuName = "Coat/Shop Stock")]
    public class CoatShopStock : ScriptableObject
    {
        public List<CosmeticDef> Items = new List<CosmeticDef>();
    }
}
