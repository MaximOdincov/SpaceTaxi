using UnityEngine;

namespace SpaceTaxi.UI
{
    public class SettingsScreenController : MonoBehaviour
    {
        public void Open() => gameObject.SetActive(true);

        public void Close() => gameObject.SetActive(false);
    }
}
