using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Unity Facebook/Contenido", fileName = "AppContent")]
public class AppContent : ScriptableObject
{
    public string nombreApp = "Unity Facebook";
    public string materia = "Desarrollo Móvil Integral";
    [TextArea(2, 5)] public string descripcion = "Una idea compartida, cinco perspectivas. Creamos esta aplicación para aprender, conectar y convertir ideas en experiencias.";
    public Integrante[] integrantes = new Integrante[5];

    [Serializable]
    public class Integrante
    {
        public string nombre = "Integrante";
        public string rol = "Integrante del equipo";
        [TextArea(3, 6)] public string biografia = "Este perfil está listo para contar tu historia. Agrega tu fotografía y una breve presentación en el contenido de la aplicación.";
        public string[] habilidades = { "Unity", "C#", "Trabajo en equipo" };
        public Texture2D fotografia;
        public Color color = new Color(0.78f, 0.91f, 0.57f);
    }
}
