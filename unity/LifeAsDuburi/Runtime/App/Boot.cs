// The one component in the LifeAsDuburi scene (added by Life as Duburi -> Set Up
// Project): starts the game (App = src/main.js) and drives it every frame. The error
// panel shows "could not start" in every build, and any later exception on screen only
// in the Editor and in Development builds (release builds log it to the console).

using System;
using Duburi.Core;
using UnityEngine;

namespace Duburi.Game
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)] // InputHub.Poll before other scripts read anything this frame
    public sealed class Boot : MonoBehaviour
    {
        App app;
        ErrorPanel errors;

        /// the running app (null if it could not start); a handle for debugging, like window.__duburi
        public App App => app;

        void Awake()
        {
            errors = new ErrorPanel(transform);
            try
            {
                app = new App(transform, errors);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                errors.Show(MenuSetup.GAME_NAME + " could not start: " + e.Message +
                            ". Run Life as Duburi > Set Up Project in the Editor; the browser needs WebGL 2.");
            }
        }

        void Update()
        {
            app?.Frame();
        }

        void LateUpdate()
        {
            app?.LateFrame();
            errors?.Update();
        }

        void OnDestroy()
        {
            if (app != null)
            {
                try { app.Dispose(); } catch (Exception e) { Debug.LogException(e); }
                app = null;
            }
            errors?.Dispose();
            errors = null;
        }
    }
}
