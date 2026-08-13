using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;

namespace Artemis.Session
{
    /// <summary>
    /// Scheda "Session": l'ingresso in aula. Finche' non si e' connessi e' l'UNICA scheda della
    /// HUD — gli altri pannelli aspettano — cosi' non c'e' modo di premere pulsanti che non
    /// avrebbero ancora senso.
    ///
    /// Due pulsanti e nessuna digitazione: il docente apre l'aula, gli studenti vi si uniscono.
    /// A connessione avvenuta i pulsanti spariscono e restano lo stato e il numero di presenti.
    /// </summary>
    public class SessionPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Session";

        private bool built;
        private float nextRefresh;
        private VrSession bound;

        private RectTransform buttonRow;
        private TMP_Text stateLabel, roleLabel, hintLabel;

        private void Update()
        {
            if (!built) { TryBuild(); return; }

            var s = VrSession.Instance;
            if (s != bound)
            {
                if (bound != null) bound.OnStateChanged -= Refresh;
                bound = s;
                if (bound != null) bound.OnStateChanged += Refresh;
            }

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.3f;
            Refresh();
        }

        private void OnDestroy() { if (bound != null) bound.OnStateChanged -= Refresh; }

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            var flow = AreaFlow.Instance;
            if (hud == null || flow == null) return;

            // L'ingresso in aula si fa dalla Base: entrare o uscire da una sessione mentre la
            // classe e' in bosco creerebbe soltanto disallineamenti.
            if (!flow.IsOnBase) { enabled = false; return; }

            var page = hud.CreateTab(tabTitle);

            stateLabel = hud.MakeLabel(page, "", 20);
            roleLabel  = hud.MakeLabel(page, "", 17);

            buttonRow = hud.MakeRow(page);
            hud.MakeButton(buttonRow, "Create\n(teacher)", () => VrSession.Instance?.CreateAsTeacher());
            hud.MakeButton(buttonRow, "Join\n(student)",  () => VrSession.Instance?.JoinAsStudent());

            hintLabel = hud.MakeLabel(page, "", 14);

            built = true;
            Refresh();
        }

        private void Refresh()
        {
            if (!built) return;
            var s = VrSession.Instance;

            if (s == null)
            {
                stateLabel.text = "session service unavailable";
                return;
            }

            bool connected = VrSession.IsConnected;
            if (buttonRow != null && buttonRow.gameObject.activeSelf == connected)
                buttonRow.gameObject.SetActive(!connected);

            switch (s.Current)
            {
                case VrSession.Phase.Working:
                    stateLabel.text = "connecting…";
                    hintLabel.text = "";
                    break;

                case VrSession.Phase.InSession:
                    stateLabel.text = $"in session · {s.PlayerCount} connected";
                    hintLabel.text = VrSession.IsTeacher
                        ? "you lead: choose the plot, everyone follows"
                        : "wait for the teacher to choose the plot";
                    break;

                case VrSession.Phase.Failed:
                    stateLabel.text = "not connected";
                    hintLabel.text = s.LastError;
                    break;

                default:
                    stateLabel.text = "not connected";
                    hintLabel.text = s.RequireSession
                        ? "the teacher creates the classroom, students join it"
                        : "solo mode: you can also work without a session";
                    break;
            }

            roleLabel.text = VrSession.LocalRole switch
            {
                VrSession.Role.Teacher => "role: TEACHER",
                VrSession.Role.Student => "role: student",
                _ => s.RequireSession ? "" : "role: solo"
            };
        }
    }
}
