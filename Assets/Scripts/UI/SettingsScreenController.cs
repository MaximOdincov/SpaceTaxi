using UnityEngine;

namespace SpaceTaxi.UI
{
    // Экран настроек (префаб): открыть/закрыть. Живёт внутри префаба SettingsUI,
    // кнопка НАЗАД внутри префаба вызывает Close().
    public class SettingsScreenController : MonoBehaviour
    {
        public void Open() => gameObject.SetActive(true);

        public void Close() => gameObject.SetActive(false);
    }
}
