using System;
using UnityEngine;

namespace Delvefolk
{
    // Persistencia local (cosméticos, habilidad elegida, ajustes del anfitrión) en PlayerPrefs con JSON.
    // Cualquier fallo se ignora y el juego sigue con los valores por defecto.
    public static class Storage
    {
        // Las pruebas usan su propio prefijo para no tocar la selección guardada del jugador
        public static string Prefix = "delvefolk.";
        // Las pruebas pueden desactivarla para no tocar las preferencias reales del jugador
        public static bool Enabled = true;

        public static T Get<T>(string key, T fallback) where T : class
        {
            if (!Enabled) return fallback;
            try
            {
                string raw = PlayerPrefs.GetString(Prefix + key, null);
                if (string.IsNullOrEmpty(raw)) return fallback;
                T v = JsonUtility.FromJson<T>(raw);
                return v ?? fallback;
            }
            catch (Exception) { return fallback; }
        }

        public static bool Set<T>(string key, T value) where T : class
        {
            if (!Enabled) return false;
            try
            {
                PlayerPrefs.SetString(Prefix + key, JsonUtility.ToJson(value));
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Delete(string key)
        {
            try { PlayerPrefs.DeleteKey(Prefix + key); } catch (Exception) { }
        }
    }
}
