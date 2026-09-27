using DtHub.Core.Touch;

namespace DtHub.Tests.Touch;

public class TouchStateReaderTests
{
    /// <summary>
    /// Real output, captured on 2026-09-27 on the Xiaomi 13T Pro of the
    /// development machine (Android 16, MIUI) while the Cra account could no
    /// longer move: <c>dumpsys input</c> passed through the very filter
    /// <see cref="TouchStateReader.Command" /> runs on the phone. Copied
    /// character for character from the dispatcher's own section to the
    /// ANR copy, plus the MIUI header that follows.
    ///
    /// Display 12 is Cra with the two fingers left down; display 13 is Iop,
    /// healthy, with only the mouse hovering over it.
    /// </summary>
    private const string Bloque = """
        Input Dispatcher State:
          DispatchEnabled: true
          DispatchFrozen: false
          InputFilterEnabled: false
          FocusedDisplayId: 12
          FocusedApplications:
          FocusedWindows:
          FocusRequests:
          Pointer Capture Requested: false
          Current Window with Pointer Capture: None
          TouchStatesByDisplay:
            12 :     Windows:
                0 : name='49c98e5 com.ankama.dofustouch/com.ankama.dofustouch.MainActivity', targetFlags=FOREGROUND | SPLIT, forwardingWindowToken=0x0, mDeviceStates=-1:[touchingPointers=[Pointer(id=0, FINGER), Pointer(id=1, FINGER)], downTimeInTarget=74220948000000, hoveringPointers=[], pilferingPointerIds=<none>]
            13 :     Windows:
                0 : name='3827f8a com.ankama.dofustouch/com.ankama.dofustouch.MainActivity', targetFlags=0x0, forwardingWindowToken=0x0, mDeviceStates=-1:[touchingPointers=[], downTimeInTarget=<not set>, hoveringPointers=[Pointer(id=0, MOUSE) at (118, 0)], pilferingPointerIds=<none>]
            11 :     Windows: <none>
            10 :     Windows: <none>
            9 :     Windows: <none>
            8 :     Windows: <none>
            7 :     Windows: <none>
            6 :     Windows: <none>
            5 :     Windows: <none>
            4 :     Windows: <none>
            3 :     Windows: <none>
            2 :     Windows: <none>
          CursorStatesByDisplay: <no displays touched by cursor>
          Display: 13
          Display: 12
          Display: 0
          DisplayTopologyGraph:
          Global monitors on display 13:
          Global monitors on display 12:
          Global monitors on display 0:
          Connections:
          RecentQueue: length=10
          PendingEvent: <none>
          InboundQueue: <empty>
          CommandQueue: <empty>
          TouchModePerDisplay:
          Configuration:
          InputTracer: Disabled
        Input Dispatcher State at time of last ANR:
          ANR:
          DispatchEnabled: true
          DispatchFrozen: false
          InputFilterEnabled: false
          FocusedDisplayId: 0
          FocusedApplications:
          FocusedWindows:
          FocusRequests:
          Pointer Capture Requested: false
          Current Window with Pointer Capture: None
          TouchStatesByDisplay: <no displays touched>
          CursorStatesByDisplay: <no displays touched by cursor>
        MiInput Dispatcher State:
        """;

    [Fact]
    public void Les_deux_doigts_restes_poses_sont_comptes_sur_leur_ecran()
    {
        var touches = TouchStateReader.Read(Bloque);

        Assert.NotNull(touches);
        Assert.Equal(2, touches[12]);
    }

    [Fact]
    public void La_souris_qui_survole_n_est_pas_un_doigt_pose()
    {
        var touches = TouchStateReader.Read(Bloque);

        Assert.NotNull(touches);
        Assert.Equal(0, touches.GetValueOrDefault(13));
    }

    [Fact]
    public void La_copie_figee_au_dernier_gel_n_est_pas_lue()
    {
        // Built from the real one: the live section says nothing is touched,
        // and only the frozen ANR copy carries fingers. Reading that copy
        // would repair, forever, a gesture that ended long ago.
        var inverse = """
            Input Dispatcher State:
              TouchStatesByDisplay: <no displays touched>
              CursorStatesByDisplay: <no displays touched by cursor>
            Input Dispatcher State at time of last ANR:
              TouchStatesByDisplay:
                12 :     Windows:
                    0 : name='49c98e5 com.ankama.dofustouch/com.ankama.dofustouch.MainActivity', mDeviceStates=-1:[touchingPointers=[Pointer(id=0, FINGER)], hoveringPointers=[]]
              CursorStatesByDisplay: <no displays touched by cursor>
            """;

        var touches = TouchStateReader.Read(inverse);

        Assert.NotNull(touches);
        Assert.Empty(touches);
    }

    [Fact]
    public void Aucun_ecran_touche_se_lit_comme_un_releve_vide_et_non_comme_une_inconnue()
    {
        var touches = TouchStateReader.Read("""
            Input Dispatcher State:
              TouchStatesByDisplay: <no displays touched>
            """);

        Assert.NotNull(touches);
        Assert.Empty(touches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MiInput Dispatcher State:\n  TouchStatesByDisplay:\n    12 :     Windows:")]
    [InlineData("Input Dispatcher State:\n  DispatchEnabled: true")]
    public void Un_releve_sans_la_section_du_repartiteur_ne_dit_rien(string dump)
    {
        // Unknown and not "nothing touched": the caller must not conclude
        // that a finger was lifted from a reading that never looked.
        Assert.Null(TouchStateReader.Read(dump));
    }

    [Fact]
    public void L_ancien_format_en_masque_de_doigts_est_compte()
    {
        // Constructed, not captured: Android before 15 writes the pointers
        // of a window as a bit mask. No phone at hand runs it, so this only
        // pins the reading of the mask, 0x3 being fingers 0 and 1.
        var touches = TouchStateReader.Read("""
            Input Dispatcher State:
              TouchStatesByDisplay:
                12 : down=true, split=true, deviceId=-1, source=0x00001002
                    0: name='49c98e5 com.ankama.dofustouch/com.ankama.dofustouch.MainActivity', targetFlags=0x105, pointerIds=0x3, firstDownTimeInTarget=74220948000000
              Display: 12
            """);

        Assert.NotNull(touches);
        Assert.Equal(2, touches[12]);
    }
}
