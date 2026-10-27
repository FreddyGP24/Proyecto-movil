using System;
using System.Collections;
using System.Net.Mail;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Single-scene mobile UI. Edit the layout in Resources/Nexo with UI Builder.</summary>
[RequireComponent(typeof(FacebookLogin))]
public class MobileAppController : MonoBehaviour
{
    [SerializeField] private AppContent contenido;
    private UIDocument documento;
    private PanelSettings panel;
    private VisualElement raiz, marco;
    private FacebookLogin facebook;
    private string vista = "splash";
    private bool sesion;
    private int perfil, ancho, alto;
    private Rect zonaSegura;
    private VisualElement enfoqueAnterior;
    private bool confirmarSalida;
    public string VistaActual => vista;

    private void Awake()
    {
        if (Application.isMobilePlatform || Application.isEditor)
            Screen.orientation = ScreenOrientation.Portrait;

        if (contenido == null) contenido = Resources.Load<AppContent>("Nexo/AppContent");
        var plantilla = Resources.Load<VisualTreeAsset>("Nexo/App");
        var tema = Resources.Load<ThemeStyleSheet>("Nexo/AppTheme");
        if (contenido == null || plantilla == null || tema == null)
        {
            Debug.LogError("No se encontraron los recursos de la interfaz en Resources/Nexo.", this);
            enabled = false;
            return;
        }
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.name = "Unity Facebook Runtime Panel";
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.themeStyleSheet = tema;
        panel.sortingOrder = 10;
        panel.clearDepthStencil = true;
        panel.clearColor = true;
        panel.colorClearValue = new Color(0.87f, 0.89f, 0.85f, 1f);
        documento = gameObject.AddComponent<UIDocument>();
        documento.panelSettings = panel;
        documento.visualTreeAsset = plantilla;
        raiz = documento.rootVisualElement;
        marco = raiz.Q("safe-area");
        facebook = GetComponent<FacebookLogin>();
        facebook.EstadoCambiado += ActualizarFacebook;
        facebook.SesionIniciada += AccederConFacebook;
#if UNITY_EDITOR
        facebook.AccesoEditorSolicitado += MostrarAccesoFacebookEditor;
        facebook.CuentasEditorCambiadas += RefrescarCuentasFacebookEditor;
#endif
        ConectarInterfaz();
        CrearEquipo();
        AjustarPantalla();
        CambiarVista("splash");
        StartCoroutine(Splash());
    }

    private IEnumerator Splash()
    {
        const float duracion = 1.6f;
        float inicio = Time.realtimeSinceStartup;
        var progreso = raiz.Q("splash-progress");
        while (Time.realtimeSinceStartup - inicio < duracion)
        {
            progreso.style.width = Length.Percent((Time.realtimeSinceStartup - inicio) / duracion * 100);
            yield return null;
        }
        CambiarVista("login");
    }

    private void ConectarInterfaz()
    {
        raiz.Query<Label>(className: "brand-name").ForEach(label => label.text = contenido.nombreApp);
        raiz.Q<Label>("splash-name").text = contenido.nombreApp;
        raiz.Q<Label>("course-name").text = contenido.materia;
        raiz.Q<Label>("team-description").text = contenido.descripcion;
        raiz.Query<Label>().ForEach(label => label.enableRichText = false);
        raiz.Q<Button>("facebook-button").clicked += facebook.IniciarSesionFacebook;
#if UNITY_EDITOR
        raiz.Q<Button>("facebook-editor-browser").clicked += facebook.AbrirAutorizacionEditor;
        raiz.Q<Button>("facebook-editor-close").clicked += CerrarAccesoFacebookEditor;
        raiz.Q<Button>("facebook-editor-cancel").clicked += CerrarAccesoFacebookEditor;
        raiz.Q<Button>("facebook-editor-add").clicked += () =>
        {
            var formulario = raiz.Q("facebook-editor-add-form");
            bool abrir = formulario.ClassListContains("hidden");
            formulario.EnableInClassList("hidden", !abrir);
            raiz.Q<Button>("facebook-editor-add").text = abrir ? "Cerrar nueva cuenta  −" : "Agregar otra cuenta  +";
            if (abrir) raiz.Q<Button>("facebook-editor-browser").Focus();
        };
        raiz.Q<Button>("facebook-editor-connect").clicked += () =>
        {
            var campo = raiz.Q<TextField>("facebook-editor-token");
            string token = campo.value;
            campo.SetValueWithoutNotify("");
            facebook.ValidarTokenEditor(token);
        };
        raiz.Q<TextField>("facebook-editor-token").isPasswordField = true;
        raiz.Q<TextField>("facebook-editor-token").autoCorrection = false;
        raiz.Q<TextField>("facebook-editor-token").maxLength = 4096;
#endif
        raiz.Q<Button>("demo-toggle").clicked += () =>
        {
            bool abrir = raiz.Q("demo-form").ClassListContains("hidden");
            raiz.Q("demo-form").EnableInClassList("hidden", !abrir);
            raiz.Q<Button>("demo-toggle").text = abrir ? "Cerrar acceso demo" : "Explorar en modo demo  →";
            if (abrir) raiz.Q<TextField>("demo-name").Focus();
        };
        raiz.Q<Button>("demo-submit").clicked += AccederDemo;
        raiz.Q<Button>("team-button").clicked += () => CambiarVista("team");
        raiz.Q<Button>("nav-home").clicked += () => CambiarVista("home");
        raiz.Q<Button>("nav-team").clicked += () => CambiarVista("team");
        raiz.Q<Button>("team-back").clicked += () => CambiarVista("home");
        raiz.Q<Button>("profile-back").clicked += () => CambiarVista("team");
        raiz.Q<Button>("profile-prev").clicked += () => MostrarPerfil((perfil + contenido.integrantes.Length - 1) % contenido.integrantes.Length);
        raiz.Q<Button>("profile-next").clicked += () => MostrarPerfil((perfil + 1) % contenido.integrantes.Length);
        raiz.Q<Button>("logout-button").clicked += () => MostrarConfirmacion(false);
        raiz.Q<Button>("exit-button").clicked += () => MostrarConfirmacion(true);
        raiz.Q<Button>("dialog-cancel").clicked += CerrarConfirmacion;
        raiz.Q<Button>("dialog-confirm").clicked += Confirmar;
        raiz.Q<TextField>("demo-email").keyboardType = TouchScreenKeyboardType.EmailAddress;
        raiz.Q<TextField>("demo-email").autoCorrection = false;
        raiz.Q<TextField>("demo-name").maxLength = 45;
        raiz.Q<TextField>("demo-email").maxLength = 100;
        raiz.RegisterCallback<FocusInEvent>(evt =>
        {
#if UNITY_EDITOR
            var accesoFacebook = raiz.Q("facebook-editor-overlay");
            if (!accesoFacebook.ClassListContains("hidden") && evt.target is VisualElement objetivo &&
                objetivo != accesoFacebook && !accesoFacebook.Contains(objetivo))
            {
                raiz.Q<Button>("facebook-editor-close").Focus();
                return;
            }
#endif
            var dialogo = raiz.Q("dialog");
            if (!raiz.Q("dialog-overlay").ClassListContains("hidden") &&
                evt.target is VisualElement destino && destino != dialogo && !dialogo.Contains(destino))
                raiz.Q<Button>("dialog-cancel").Focus();
        });
        raiz.Q("dialog").RegisterCallback<NavigationMoveEvent>(evt =>
        {
            if (evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous)
            {
                var cancelar = raiz.Q<Button>("dialog-cancel");
                var aceptar = raiz.Q<Button>("dialog-confirm");
                if (raiz.focusController.focusedElement == cancelar) aceptar.Focus();
                else cancelar.Focus();
                evt.StopPropagation();
            }
        });
    }

    private void CrearEquipo()
    {
        var lista = raiz.Q("team-list");
        lista.Clear();
        if (contenido.integrantes == null || contenido.integrantes.Length == 0)
        {
            lista.Add(new Label("Agrega integrantes en el asset AppContent."));
            return;
        }
        for (int i = 0; i < contenido.integrantes.Length; i++)
        {
            int indice = i;
            var persona = contenido.integrantes[i];
            var boton = new Button(() => MostrarPerfil(indice));
            boton.AddToClassList("member-card");
            var avatar = new VisualElement();
            avatar.AddToClassList("member-avatar");
            AplicarAvatar(avatar, persona);
            boton.Add(avatar);
            var textos = new VisualElement();
            textos.AddToClassList("member-copy");
            textos.Add(Etiqueta("0" + (i + 1) + " / EL EQUIPO", "eyebrow"));
            textos.Add(Etiqueta(persona.nombre, "member-name"));
            textos.Add(Etiqueta(persona.rol, "muted"));
            boton.Add(textos);
            boton.Add(Etiqueta("↗", "member-arrow"));
            lista.Add(boton);
        }
        raiz.Q<Label>("team-count").text = contenido.integrantes.Length.ToString("00") + " PERSONAS · UNA VISIÓN";
    }

    private static Label Etiqueta(string texto, string clase)
    {
        var etiqueta = new Label(texto) { enableRichText = false };
        etiqueta.AddToClassList(clase);
        return etiqueta;
    }

    private static string Iniciales(string texto)
    {
        var partes = (texto ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return "UF";
        return (partes[0].Substring(0, 1) + (partes.Length > 1 ? partes[partes.Length - 1].Substring(0, 1) : "")).ToUpperInvariant();
    }

    private static void AplicarAvatar(VisualElement avatar, AppContent.Integrante persona)
    {
        avatar.Clear();
        avatar.style.backgroundColor = persona.color;
        avatar.style.backgroundImage = persona.fotografia != null ? new StyleBackground(persona.fotografia) : StyleKeyword.None;
        if (persona.fotografia == null)
        {
            var figura = new VisualElement();
            figura.AddToClassList("avatar-figure");
            avatar.Add(figura);
            var cabeza = new VisualElement();
            cabeza.AddToClassList("avatar-head");
            avatar.Add(cabeza);
            avatar.Add(Etiqueta(Iniciales(persona.nombre), "avatar-initials"));
        }
    }

    private void AccederDemo()
    {
        string nuevoNombre = raiz.Q<TextField>("demo-name").value.Trim();
        string nuevoCorreo = raiz.Q<TextField>("demo-email").value.Trim();
        var error = raiz.Q<Label>("demo-error");
        if (nuevoNombre.Length < 2)
        {
            error.text = "Escribe un nombre de al menos 2 caracteres.";
            raiz.Q<TextField>("demo-name").Focus();
            return;
        }
        if (nuevoCorreo.Length > 0 && !CorreoValido(nuevoCorreo))
        {
            error.text = "Revisa el correo o déjalo vacío para continuar.";
            raiz.Q<TextField>("demo-email").Focus();
            return;
        }
        error.text = "";
        facebook.CerrarSesion();
        AbrirSesion(nuevoNombre, nuevoCorreo, true);
    }

    private static bool CorreoValido(string valor)
    {
        try { return new MailAddress(valor).Address == valor && valor.Contains("."); }
        catch (FormatException) { return false; }
    }

    private void AccederConFacebook(string usuario)
    {
#if UNITY_EDITOR
        OcultarAccesoFacebookEditor();
#endif
        AbrirSesion(usuario, "Cuenta de Facebook", false);
    }

#if UNITY_EDITOR
    private void MostrarAccesoFacebookEditor()
    {
        raiz.Q<TextField>("facebook-editor-token").SetValueWithoutNotify("");
        raiz.Q<Label>("facebook-editor-status").text = "";
        raiz.Q("facebook-editor-overlay").RemoveFromClassList("hidden");
        raiz.Q("app-content").SetEnabled(false);
        raiz.Q("facebook-editor-overlay").Q<ScrollView>().scrollOffset = Vector2.zero;
        RefrescarCuentasFacebookEditor();
        var continuar = raiz.Q("facebook-editor-accounts").Q<Button>(className: "facebook-account-continue");
        if (continuar != null) continuar.Focus();
        else raiz.Q<Button>("facebook-editor-browser").Focus();
    }

    private void RefrescarCuentasFacebookEditor()
    {
        var lista = raiz.Q("facebook-editor-accounts");
        lista.Clear();
        var cuentas = facebook.ObtenerCuentasEditor();
        raiz.Q("facebook-editor-saved").EnableInClassList("hidden", cuentas.Count == 0);
        raiz.Q("facebook-editor-add-form").EnableInClassList("hidden", cuentas.Count > 0);
        raiz.Q<Button>("facebook-editor-add").text = "Agregar otra cuenta  +";
        raiz.Q<Label>("facebook-editor-title").text = cuentas.Count > 0 ? "Selecciona\ntu cuenta." : "Conecta tu\ncuenta de Facebook.";
        raiz.Q<Label>("facebook-editor-description").text = cuentas.Count > 0
            ? "Continúa con una cuenta que autorizaste en este editor."
            : "Autoriza tu cuenta en Meta y vuelve aquí para continuar con tu perfil.";
        foreach (var cuenta in cuentas)
        {
            string id = cuenta.Id;
            var tarjeta = new VisualElement();
            tarjeta.AddToClassList("facebook-account");
            var encabezado = new VisualElement();
            encabezado.AddToClassList("facebook-account-heading");
            encabezado.Add(Etiqueta(Iniciales(cuenta.Nombre), "facebook-account-avatar"));
            var textos = new VisualElement();
            textos.AddToClassList("facebook-account-copy");
            textos.Add(Etiqueta(cuenta.Nombre, "facebook-account-name"));
            textos.Add(Etiqueta("Cuenta de Facebook", "caption"));
            encabezado.Add(textos);
            tarjeta.Add(encabezado);
            var entrar = new Button(() => facebook.EntrarCuentaEditor(id)) { text = "Continuar como " + cuenta.Nombre };
            entrar.AddToClassList("button");
            entrar.AddToClassList("facebook-editor-connect");
            entrar.AddToClassList("facebook-account-continue");
            tarjeta.Add(entrar);
            var quitar = new Button(() => facebook.OlvidarCuentaEditor(id)) { text = "Quitar de este editor" };
            quitar.AddToClassList("text-button");
            quitar.AddToClassList("facebook-account-forget");
            tarjeta.Add(quitar);
            lista.Add(tarjeta);
        }
    }

    private void OcultarAccesoFacebookEditor()
    {
        raiz.Q("facebook-editor-overlay").AddToClassList("hidden");
        raiz.Q<TextField>("facebook-editor-token").SetValueWithoutNotify("");
        raiz.Q("app-content").SetEnabled(true);
    }

    private void CerrarAccesoFacebookEditor()
    {
        facebook.CancelarAccesoEditor();
        OcultarAccesoFacebookEditor();
        raiz.Q<Button>("facebook-button").Focus();
    }
#endif

    private void AbrirSesion(string nombre, string correo, bool demo)
    {
        sesion = true;
        raiz.Q<Label>("welcome-name").text = "Hola, " + nombre.Split(' ')[0] + ".";
        raiz.Q<Label>("user-name").text = nombre;
        raiz.Q<Label>("user-email").text = string.IsNullOrEmpty(correo) ? "Explorando en modo demo" : correo;
        raiz.Q<Label>("user-avatar").text = Iniciales(nombre);
        raiz.Q<Label>("session-note").text = demo ? "Modo demo. Los datos de acceso no se guardan." : "Facebook conectado. Tu sesión está activa.";
#if UNITY_EDITOR
        if (!demo) raiz.Q<Label>("session-note").text = "Perfil validado con Facebook para esta prueba en Unity.";
#endif
        CambiarVista("home");
    }

    private void ActualizarFacebook(string mensaje, bool ocupado)
    {
        raiz.Q<Label>("login-status").text = mensaje;
        raiz.Q<Button>("facebook-button").SetEnabled(!ocupado);
        raiz.Q<Label>("facebook-label").text = ocupado ? "Conectando…" : "Continuar con Facebook";
#if UNITY_EDITOR
        raiz.Q<Label>("facebook-editor-status").text = mensaje;
        raiz.Q<Button>("facebook-editor-connect").text = ocupado ? "Validando tu cuenta…" : "Guardar y continuar  →";
        raiz.Q<Button>("facebook-editor-connect").SetEnabled(!ocupado);
        raiz.Q<Button>("facebook-editor-browser").SetEnabled(!ocupado);
        raiz.Q<TextField>("facebook-editor-token").SetEnabled(!ocupado);
        raiz.Q("facebook-editor-accounts").SetEnabled(!ocupado);
        raiz.Q<Button>("facebook-editor-add").SetEnabled(!ocupado);
        if (!ocupado && !string.IsNullOrEmpty(mensaje)) raiz.Q("facebook-editor-add-form").RemoveFromClassList("hidden");
#endif
    }

    public void CambiarVista(string nueva)
    {
        if (nueva != "splash" && nueva != "login" && !sesion) nueva = "login";
        vista = nueva;
        foreach (string pantalla in new[] { "splash", "login", "home", "team", "profile" })
            raiz.Q("screen-" + pantalla).EnableInClassList("hidden", pantalla != nueva);
        raiz.Q("bottom-nav").EnableInClassList("hidden", nueva == "splash" || nueva == "login");
        raiz.Q<Button>("nav-home").EnableInClassList("nav-active", nueva == "home");
        raiz.Q<Button>("nav-team").EnableInClassList("nav-active", nueva == "team" || nueva == "profile");
        raiz.Q("app").EnableInClassList("splash-mode", nueva == "splash");
        raiz.Query<ScrollView>().ForEach(scroll => scroll.scrollOffset = Vector2.zero);
        if (nueva != "splash")
            raiz.schedule.Execute(() =>
            {
                string destino = vista == "login" ? "facebook-button" : vista == "home" ? "team-button" : vista == "team" ? "team-back" : "profile-back";
                raiz.Q<Button>(destino)?.Focus();
            });
    }

    public void MostrarPerfil(int indice)
    {
        if (!sesion || contenido.integrantes == null || indice < 0 || indice >= contenido.integrantes.Length) return;
        perfil = indice;
        var persona = contenido.integrantes[indice];
        raiz.Q<Label>("profile-name").text = persona.nombre;
        raiz.Q<Label>("profile-role").text = persona.rol;
        raiz.Q<Label>("profile-bio").text = persona.biografia;
        raiz.Q<Label>("profile-number").text = (indice + 1).ToString("00");
        raiz.Q<Label>("profile-pagination").text = (indice + 1).ToString("00") + " / " + contenido.integrantes.Length.ToString("00");
        raiz.Q<Label>("profile-course").text = contenido.materia;
        raiz.Q<Label>("profile-art-caption").text = "EL EQUIPO / " + contenido.nombreApp.ToUpperInvariant();
        AplicarAvatar(raiz.Q("profile-art"), persona);
        var habilidades = raiz.Q("profile-skills");
        habilidades.Clear();
        if (persona.habilidades != null)
            foreach (string habilidad in persona.habilidades) habilidades.Add(Etiqueta(habilidad, "skill"));
        CambiarVista("profile");
    }

    private void MostrarConfirmacion(bool salir)
    {
        confirmarSalida = salir;
        enfoqueAnterior = raiz.focusController.focusedElement as VisualElement;
        raiz.Q<Label>("dialog-title").text = salir ? "¿Nos vemos pronto?" : "¿Cerrar tu sesión?";
        raiz.Q<Label>("dialog-description").text = salir ? "Se cerrará la aplicación. Puedes volver cuando quieras." : "Volverás a la pantalla de acceso.";
        raiz.Q<Button>("dialog-confirm").text = salir ? "Salir" : "Cerrar sesión";
        raiz.Q("dialog-overlay").RemoveFromClassList("hidden");
        raiz.Q<Button>("dialog-cancel").Focus();
    }

    private void CerrarConfirmacion()
    {
        raiz.Q("dialog-overlay").AddToClassList("hidden");
        enfoqueAnterior?.Focus();
    }

    private void Confirmar()
    {
        CerrarConfirmacion();
        if (confirmarSalida)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return;
        }
        facebook.CerrarSesion();
        sesion = false;
        raiz.Q<TextField>("demo-name").value = "";
        raiz.Q<TextField>("demo-email").value = "";
        raiz.Q<Label>("demo-error").text = "";
        raiz.Q("demo-form").AddToClassList("hidden");
        raiz.Q<Button>("demo-toggle").text = "Explorar en modo demo  →";
        CambiarVista("login");
    }

    private void Update()
    {
        if (Screen.width != ancho || Screen.height != alto || Screen.safeArea != zonaSegura) AjustarPantalla();
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Volver();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) Volver();
#endif
    }

    private void Volver()
    {
#if UNITY_EDITOR
        if (!raiz.Q("facebook-editor-overlay").ClassListContains("hidden"))
        {
            CerrarAccesoFacebookEditor();
            return;
        }
#endif
        if (!raiz.Q("dialog-overlay").ClassListContains("hidden")) CerrarConfirmacion();
        else if (vista == "profile") CambiarVista("team");
        else if (vista == "team") CambiarVista("home");
        else if (vista != "splash") MostrarConfirmacion(true);
    }

    private void AjustarPantalla()
    {
        ancho = Screen.width;
        alto = Screen.height;
        zonaSegura = Screen.safeArea;
        float escala = Mathf.Clamp(Mathf.Min(ancho, alto) / 440f, 0.65f, 2.5f);
        panel.scale = escala;
        marco.style.paddingTop = (alto - zonaSegura.yMax) / escala;
        marco.style.paddingBottom = zonaSegura.y / escala;
        marco.style.paddingLeft = zonaSegura.x / escala;
        marco.style.paddingRight = (ancho - zonaSegura.xMax) / escala;
        raiz.Q("app").EnableInClassList("compact", alto / escala < 710);
    }

    private void OnDestroy()
    {
        if (facebook != null)
        {
            facebook.EstadoCambiado -= ActualizarFacebook;
            facebook.SesionIniciada -= AccederConFacebook;
#if UNITY_EDITOR
            facebook.AccesoEditorSolicitado -= MostrarAccesoFacebookEditor;
            facebook.CuentasEditorCambiadas -= RefrescarCuentasFacebookEditor;
#endif
        }
        if (panel != null) Destroy(panel);
    }
}
