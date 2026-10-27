using System;
using System.Collections;
using System.Collections.Generic;
using Facebook.Unity;
using Facebook.Unity.Settings;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.Networking;
#endif

/// <summary>Facebook authentication. Navigation and demo sessions belong to the interface.</summary>
public class FacebookLogin : MonoBehaviour
{
    public event Action<string, bool> EstadoCambiado;
    public event Action<string> SesionIniciada;
    public bool IniciandoSesion { get; private set; }
    private int solicitud;
    private Coroutine limiteEspera;
#if UNITY_EDITOR
    public event Action AccesoEditorSolicitado;
    public event Action CuentasEditorCambiadas;
    private UnityWebRequest peticionEditor;
    private Coroutine rutinaEditor;
#endif

    public void IniciarSesionFacebook()
    {
        if (IniciandoSesion) return;
        if (!ulong.TryParse(FacebookSettings.AppId, out ulong appId) || appId == 0 ||
            string.IsNullOrWhiteSpace(FacebookSettings.ClientToken))
        {
            Informar("Falta configurar el App ID y el Client Token en Facebook > Edit Settings. Puedes explorar con el modo demo.", false);
            return;
        }
#if UNITY_EDITOR
        // The Device Simulator cannot run Facebook's native phone login UI.
        // Our responsive test screen validates a user token with Graph instead of opening the SDK's IMGUI mock.
        Informar("", false);
        AccesoEditorSolicitado?.Invoke();
#else
        IniciandoSesion = true;
        int actual = ++solicitud;
        Informar("Conectando con Facebook…", true);
        limiteEspera = StartCoroutine(EsperarRespuesta(actual));
        try
        {
            if (FB.IsInitialized) SolicitarPermisos(actual);
            else FB.Init(() => SolicitarPermisos(actual), visible => Time.timeScale = visible ? 1f : 0f);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("No se pudo iniciar Facebook: " + exception.Message);
            Finalizar("No se pudo conectar. Revisa la configuración de Facebook o prueba el modo demo.");
        }
#endif
    }

#if UNITY_EDITOR
    public sealed class CuentaEditor
    {
        public string Id { get; }
        public string Nombre { get; }
        public CuentaEditor(string id, string nombre) { Id = id; Nombre = nombre; }
    }

    public List<CuentaEditor> ObtenerCuentasEditor()
    {
        var cuentas = new List<CuentaEditor>();
        try
        {
            foreach (var cuenta in FacebookEditorAccountStore.Read()) cuentas.Add(new CuentaEditor(cuenta.id, cuenta.name));
        }
        catch (Exception)
        {
            Informar("No se pudieron leer las cuentas guardadas de este usuario de Windows.", false);
        }
        return cuentas;
    }

    public void EntrarCuentaEditor(string id)
    {
        if (IniciandoSesion) return;
        try
        {
            var cuenta = FacebookEditorAccountStore.Read().Find(item => item.id == id);
            if (cuenta != null) { ValidarTokenEditor(cuenta.token); return; }
            Informar("Esta cuenta ya no está guardada. Vuelve a autorizarla para continuar.", false);
        }
        catch (Exception) { Informar("No se pudo abrir la cuenta guardada en este equipo.", false); }
    }

    public void OlvidarCuentaEditor(string id)
    {
        if (IniciandoSesion) return;
        try
        {
            FacebookEditorAccountStore.Forget(id);
            Informar("", false);
            CuentasEditorCambiadas?.Invoke();
        }
        catch (Exception) { Informar("No se pudo quitar la cuenta guardada.", false); }
    }

    public void AbrirAutorizacionEditor() => Application.OpenURL("https://developers.facebook.com/tools/explorer/");

    public void ValidarTokenEditor(string token)
    {
        if (IniciandoSesion) return;
        token = (token ?? "").Trim();
        bool formatoValido = true;
        foreach (char caracter in token)
            if (!(caracter >= 'a' && caracter <= 'z') && !(caracter >= 'A' && caracter <= 'Z') &&
                !(caracter >= '0' && caracter <= '9') && "+/=_-|.".IndexOf(caracter) < 0) formatoValido = false;
        if (token.Length < 20 || token.Length > 4096 || !formatoValido ||
            token == FacebookSettings.ClientToken || token == FacebookSettings.AppId)
        {
            Informar("Pega un token de acceso de usuario generado en Meta para esta aplicación.", false);
            return;
        }
        IniciandoSesion = true;
        int actual = ++solicitud;
        Informar("Validando tu cuenta con Facebook…", true);
        rutinaEditor = StartCoroutine(ConsultarPerfilEditor(token, actual));
    }

    private IEnumerator ConsultarPerfilEditor(string token, int actual)
    {
        var peticion = UnityWebRequest.Get("https://graph.facebook.com/" + FB.GraphApiVersion + "/me?fields=id,name");
        peticionEditor = peticion;
        peticion.timeout = 30;
        peticion.redirectLimit = 0;
        // Keep the credential out of URLs, logs, PlayerPrefs and project assets.
        peticion.SetRequestHeader("Authorization", "Bearer " + token);
        yield return peticion.SendWebRequest();
        if (this == null || actual != solicitud) yield break;
        bool correcto = peticion.result == UnityWebRequest.Result.Success;
        string respuesta = correcto ? peticion.downloadHandler.text : "";
        long codigo = peticion.responseCode;
        peticion.Dispose();
        peticionEditor = null;
        rutinaEditor = null;
        if (!correcto)
        {
            Finalizar(codigo == 400 || codigo == 401 || codigo == 403
                ? "El token no es válido o caducó. Genera otro token de usuario en Meta."
                : "No se pudo validar la cuenta. Comprueba tu conexión e inténtalo de nuevo.");
            yield break;
        }
        PerfilEditor perfil = null;
        try { perfil = JsonUtility.FromJson<PerfilEditor>(respuesta); }
        catch (ArgumentException) { }
        if (perfil == null || string.IsNullOrWhiteSpace(perfil.id) || string.IsNullOrWhiteSpace(perfil.name))
        {
            Finalizar("Facebook no devolvió un perfil válido. Comprueba el permiso public_profile.");
            yield break;
        }
        bool guardada = false;
        try { FacebookEditorAccountStore.Save(perfil.id, perfil.name, token); guardada = true; }
        catch (Exception) { }
        if (!guardada)
        {
            Finalizar("La cuenta es válida, pero no se pudo guardar cifrada en este equipo.");
            yield break;
        }
        Finalizar("");
        CuentasEditorCambiadas?.Invoke();
        SesionIniciada?.Invoke(perfil.name);
    }

    [Serializable]
    private class PerfilEditor { public string id; public string name; }

    public void CancelarAccesoEditor()
    {
        ++solicitud;
        if (rutinaEditor != null) StopCoroutine(rutinaEditor);
        rutinaEditor = null;
        if (peticionEditor != null)
        {
            peticionEditor.Abort();
            peticionEditor.Dispose();
            peticionEditor = null;
        }
        Finalizar("");
    }
#endif

    private void SolicitarPermisos(int actual)
    {
        if (this == null || actual != solicitud) return;
        if (!FB.IsInitialized)
        {
            Finalizar("Facebook no pudo inicializarse. Revisa la configuración del SDK.");
            return;
        }
        FB.ActivateApp();
        if (FB.IsLoggedIn) ObtenerPerfil(actual);
        else FB.LogInWithReadPermissions(new List<string> { "public_profile" }, resultado =>
        {
            if (this == null || actual != solicitud) return;
            if (resultado == null || resultado.Cancelled)
                Finalizar("Acceso cancelado. Puedes intentarlo de nuevo.");
            else if (!string.IsNullOrEmpty(resultado.Error) || !FB.IsLoggedIn)
                Finalizar("No se pudo iniciar sesión. Revisa tu conexión y la configuración de Facebook.");
            else ObtenerPerfil(actual);
        });
    }

    private void ObtenerPerfil(int actual)
    {
        FB.API("/me?fields=name", HttpMethod.GET, resultado =>
        {
            if (this == null || actual != solicitud || !FB.IsLoggedIn) return;
            string nombre = "Usuario de Facebook";
            if (resultado != null && string.IsNullOrEmpty(resultado.Error) &&
                resultado.ResultDictionary != null &&
                resultado.ResultDictionary.TryGetValue("name", out object valor) && valor != null)
                nombre = valor.ToString();
            Finalizar("");
            SesionIniciada?.Invoke(nombre);
        });
    }

    private IEnumerator EsperarRespuesta(int actual)
    {
        yield return new WaitForSecondsRealtime(90);
        limiteEspera = null;
        if (actual == solicitud)
        {
            ++solicitud;
            Finalizar("Facebook tardó demasiado en responder. Comprueba tu conexión e inténtalo de nuevo.");
        }
    }

    public void CerrarSesion()
    {
#if UNITY_EDITOR
        CancelarAccesoEditor();
#endif
        ++solicitud;
        if (FB.IsInitialized && FB.IsLoggedIn) FB.LogOut();
        Time.timeScale = 1f;
        Finalizar("");
    }

    private void Finalizar(string mensaje)
    {
        if (limiteEspera != null) StopCoroutine(limiteEspera);
        limiteEspera = null;
        IniciandoSesion = false;
        Informar(mensaje, false);
    }

    private void Informar(string mensaje, bool ocupado) => EstadoCambiado?.Invoke(mensaje, ocupado);
    private void OnDestroy()
    {
#if UNITY_EDITOR
        CancelarAccesoEditor();
#endif
        ++solicitud;
        Time.timeScale = 1f;
    }
}
