using System.Collections.Generic; // список тел
using UnityEngine; // векторы
using SpaceTaxi.World; // тела

namespace SpaceTaxi.Physics
{
    // Реестр небесных тел + суперпозиция притяжения 1/d² (собственная физика игры)
    public class GravityField : MonoBehaviour // один реестр на сцену
    {
        private readonly List<CelestialBody> bodies = new List<CelestialBody>(); // живые тела

        public IReadOnlyList<CelestialBody> Bodies => bodies; // доступ для поиска тел

        public void Register(CelestialBody body) // тело включилось — в реестр
        {
            if (body != null && !bodies.Contains(body)) // защита от дублей
                bodies.Add(body);
        }

        public void Unregister(CelestialBody body) // тело выключилось — из реестра
        {
            bodies.Remove(body);
        }

        public Vector2 GetAccelerationAt(Vector2 position, PhysicsConfig config) // ускорение в точке
        {
            Vector2 accel = Vector2.zero; // сумма вкладов
            foreach (CelestialBody body in bodies) // по всем телам
            {
                Vector2 delta = (Vector2)body.transform.position - position; // вектор к телу
                float d2 = delta.sqrMagnitude; // квадрат дистанции
                if (d2 < 0.0001f) // слишком близко к центру
                    continue;
                float d = Mathf.Sqrt(d2); // дистанция
                float a = Mathf.Min(config.GravParamOf(body) / d2, config.GravityAccelClamp); // 1/d² с клампом баланса
                accel += delta / d * a; // вклад тела
            }
            return accel;
        }
    }
}
