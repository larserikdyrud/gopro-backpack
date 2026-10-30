using GoProBackpack.Camera;
using GoProBackpack.Control;
using GoProBackpack.Crsf;

namespace GoProBackpack.Tests;

public class RecordControllerTests
{
    private const int On = 1811;
    private const int Off = 172;
    private const int Mid = 992;

    /// <summary>Drives the controller in 50 ms ticks with a simulated radio link and camera.</summary>
    private sealed class Sim
    {
        public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

        private readonly int[] _channels = Enumerable.Repeat(Mid, RcChannels.Count).ToArray();
        private int[] _lastFrame = Enumerable.Repeat(Mid, RcChannels.Count).ToArray();
        private DateTimeOffset? _lastFrameAt;

        public RecordController Controller { get; } = new(new BackpackOptions());
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;
        public bool LinkUp { get; set; } = true;
        public bool CameraOnline { get; set; } = true;
        public bool Encoding { get; set; }
        public bool Busy { get; set; }
        /// <summary>When true the camera obeys start/stop; when false it ignores them.</summary>
        public bool CameraObeys { get; set; } = true;
        public List<(DateTimeOffset At, CameraCommand Command)> Sent { get; } = [];

        public int Rec { get => _channels[5]; set => _channels[5] = value; }
        public int Mark { get => _channels[6]; set => _channels[6] = value; }

        public int Count(CameraCommand c) => Sent.Count(s => s.Command == c);

        public void Run(TimeSpan duration)
        {
            for (var t = TimeSpan.Zero; t < duration; t += Tick)
                Step();
        }

        public IReadOnlyList<CameraCommand> Step()
        {
            Now += Tick;
            // Like RcState: the Pi only ever sees the channels from the last frame that arrived.
            if (LinkUp)
            {
                _lastFrameAt = Now;
                _lastFrame = (int[])_channels.Clone();
            }

            var camera = CameraOnline
                ? new CameraSnapshot { At = Now, Encoding = Encoding, Busy = Busy }
                : null;
            var commands = Controller.Tick(new RcSample((int[])_lastFrame.Clone(), _lastFrameAt), camera, Now);

            foreach (var c in commands)
            {
                Sent.Add((Now, c));
                if (CameraObeys && c == CameraCommand.ShutterStart) Encoding = true;
                if (CameraObeys && c == CameraCommand.ShutterStop) Encoding = false;
            }
            return commands;
        }

        /// <summary>Boot with the switch in the given position and let the link settle.</summary>
        public static Sim Settled(int rec, bool encoding = false)
        {
            var sim = new Sim { Rec = rec, Mark = Off, Encoding = encoding };
            sim.Run(TimeSpan.FromSeconds(3));
            return sim;
        }
    }

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    // Rule 1: start

    [Fact]
    public void Rec_on_starts_recording()
    {
        var sim = Sim.Settled(Off);

        sim.Rec = On;
        sim.Run(Ms(100));

        Assert.Equal(1, sim.Count(CameraCommand.ShutterStart));
        Assert.True(sim.Encoding);
        Assert.False(sim.Controller.StartPending);
    }

    [Fact]
    public void Start_is_retried_every_2_s_until_the_camera_confirms()
    {
        var sim = Sim.Settled(Off);
        sim.CameraObeys = false;

        sim.Rec = On;
        sim.Run(S(5));

        Assert.Equal(3, sim.Count(CameraCommand.ShutterStart)); // t = 0, 2, 4 s
        Assert.True(sim.Controller.StartPending);
        Assert.Equal(3, sim.Controller.ConsecutiveStartAttempts);

        sim.Encoding = true;
        sim.Step();
        Assert.False(sim.Controller.StartPending);
        Assert.Equal(0, sim.Controller.ConsecutiveStartAttempts);
    }

    [Fact]
    public void Start_waits_while_camera_is_busy_or_offline()
    {
        var sim = Sim.Settled(Off);
        sim.Busy = true;
        sim.Rec = On;
        sim.Run(S(1));
        Assert.Equal(0, sim.Count(CameraCommand.ShutterStart));

        sim.Busy = false;
        sim.CameraOnline = false;
        sim.Run(S(1));
        Assert.Equal(0, sim.Count(CameraCommand.ShutterStart));

        sim.CameraOnline = true;
        sim.Step();
        Assert.Equal(1, sim.Count(CameraCommand.ShutterStart));
    }

    [Fact]
    public void Recording_stopped_on_the_camera_is_restarted_while_rec_is_on()
    {
        var sim = Sim.Settled(On);
        Assert.True(sim.Encoding);

        sim.Encoding = false; // operator presses the camera button (T9)
        sim.Run(S(2));

        Assert.True(sim.Encoding);
        Assert.Equal(2, sim.Count(CameraCommand.ShutterStart));
    }

    [Fact]
    public void Start_keeps_retrying_during_link_loss_with_latched_rec_on()
    {
        var sim = Sim.Settled(On);
        sim.LinkUp = false;
        sim.Run(S(5));

        sim.Encoding = false; // camera stops by itself while the link is down (T20)
        sim.Run(S(2));

        Assert.True(sim.Encoding);
        Assert.Equal(2, sim.Count(CameraCommand.ShutterStart));
    }

    // Rule 2: stop

    [Fact]
    public void Rec_off_held_1_s_stops_recording()
    {
        var sim = Sim.Settled(On);

        sim.Rec = Off;
        sim.Run(Ms(950));
        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));

        sim.Run(Ms(100));
        Assert.Equal(1, sim.Count(CameraCommand.ShutterStop));
        Assert.False(sim.Encoding);
    }

    [Fact]
    public void Rec_off_shorter_than_1_s_does_not_stop()
    {
        var sim = Sim.Settled(On);

        sim.Rec = Off;
        sim.Run(Ms(900));
        sim.Rec = On;
        sim.Run(S(3));

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop)); // T3
        Assert.True(sim.Encoding);
    }

    [Fact]
    public void Mid_values_do_not_change_the_switch_state()
    {
        var sim = Sim.Settled(On);

        sim.Rec = 1000; // between the hysteresis thresholds
        sim.Run(S(3));

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
    }

    [Fact]
    public void Stop_needs_a_link_that_has_been_stable_for_2_s()
    {
        var sim = Sim.Settled(On);
        sim.LinkUp = false;
        sim.Run(S(1));

        sim.LinkUp = true;
        sim.Run(S(1)); // link OK but not yet stable
        sim.Rec = Off;
        sim.Run(S(3));

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
    }

    [Fact]
    public void Link_loss_during_the_hold_cancels_the_stop()
    {
        var sim = Sim.Settled(On);

        sim.Rec = Off;
        sim.Run(Ms(600));
        sim.LinkUp = false;
        sim.Run(Ms(600));
        sim.LinkUp = true;
        sim.Run(S(5));

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
        Assert.True(sim.Encoding);
    }

    [Fact]
    public void Stop_is_retried_3_times_1_s_apart_then_reports_stop_error()
    {
        var sim = Sim.Settled(On);
        sim.CameraObeys = false;

        sim.Rec = Off;
        sim.Run(S(5));

        var stops = sim.Sent.Where(s => s.Command == CameraCommand.ShutterStop).Select(s => s.At).ToList();
        Assert.Equal(3, stops.Count);
        Assert.Equal(S(1), stops[1] - stops[0]);
        Assert.Equal(S(1), stops[2] - stops[1]);
        Assert.True(sim.Controller.StopFailed);

        sim.Encoding = false; // operator stops it by hand
        sim.Step();
        Assert.False(sim.Controller.StopFailed);
    }

    [Fact]
    public void Stop_error_clears_when_rec_goes_back_on()
    {
        var sim = Sim.Settled(On);
        sim.CameraObeys = false;
        sim.Rec = Off;
        sim.Run(S(5));
        Assert.True(sim.Controller.StopFailed);

        sim.Rec = On;
        sim.Step();

        Assert.False(sim.Controller.StopFailed);
    }

    // Rules 3 and 4: link loss and link back

    [Fact]
    public void Switch_moved_off_during_link_loss_never_stops()
    {
        var sim = Sim.Settled(On);

        sim.LinkUp = false;
        sim.Rec = Off;
        sim.Run(S(60));
        sim.LinkUp = true;
        sim.Run(S(10)); // T4

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
        Assert.True(sim.Encoding);
    }

    [Fact]
    public void After_link_back_the_first_definite_value_is_the_reference()
    {
        var sim = Sim.Settled(On);
        sim.LinkUp = false;
        sim.Run(S(2));

        // Frames return with REC between the thresholds, so the latched ON is not a fresh reading.
        sim.Rec = Mid;
        sim.LinkUp = true;
        sim.Run(S(3));
        sim.Rec = Off;
        sim.Run(S(3));

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
    }

    [Fact]
    public void After_link_back_on_then_off_stops_normally()
    {
        var sim = Sim.Settled(On);
        sim.LinkUp = false;
        sim.Rec = Off;
        sim.Run(S(5));
        sim.LinkUp = true;
        sim.Run(S(3));

        sim.Rec = On;
        sim.Run(Ms(200));
        sim.Rec = Off;
        sim.Run(Ms(1100));

        Assert.Equal(1, sim.Count(CameraCommand.ShutterStop));
    }

    [Fact]
    public void An_outage_no_tick_saw_still_resets_the_stable_timer()
    {
        var sim = Sim.Settled(On);
        var channels = Enumerable.Repeat(Mid, RcChannels.Count).ToArray();
        channels[5] = Off;

        // The tick loop stalls for 1 s while frames stop; frames then resume with REC moved off.
        for (var now = sim.Now + S(1); now < sim.Now + S(4); now += Sim.Tick)
        {
            var commands = sim.Controller.Tick(new RcSample(channels, now), new CameraSnapshot { At = now, Encoding = true }, now);
            Assert.DoesNotContain(CameraCommand.ShutterStop, commands);
        }
    }

    // Rule 5: service start

    [Fact]
    public void Autostart_recording_with_rec_off_continues()
    {
        var sim = Sim.Settled(Off, encoding: true);
        sim.Run(S(10));

        Assert.Empty(sim.Sent);
        Assert.True(sim.Encoding);
    }

    [Fact]
    public void Autostart_recording_stops_after_on_then_off()
    {
        var sim = Sim.Settled(Off, encoding: true);

        sim.Rec = On;
        sim.Run(Ms(200));
        sim.Rec = Off;
        sim.Run(Ms(1100));

        Assert.Equal(1, sim.Count(CameraCommand.ShutterStop));
        Assert.Equal(0, sim.Count(CameraCommand.ShutterStart));
    }

    [Fact]
    public void Service_start_with_rec_on_and_camera_recording_sends_nothing()
    {
        var sim = Sim.Settled(On, encoding: true);
        sim.Run(S(5));

        Assert.Empty(sim.Sent);
    }

    // Rule 6: mark

    [Fact]
    public void Mark_press_while_recording_sends_one_hilight()
    {
        var sim = Sim.Settled(On);

        sim.Mark = On;
        sim.Run(Ms(500));
        sim.Mark = Off;
        sim.Run(Ms(200));

        Assert.Equal(1, sim.Count(CameraCommand.Hilight));
    }

    [Fact]
    public void Mark_press_while_not_recording_is_ignored()
    {
        var sim = Sim.Settled(Off);

        sim.Mark = On;
        sim.Run(Ms(500));

        Assert.Equal(0, sim.Count(CameraCommand.Hilight));
    }

    [Fact]
    public void Mark_held_across_link_loss_is_not_a_press()
    {
        var sim = Sim.Settled(On);
        sim.LinkUp = false;
        sim.Mark = On;
        sim.Run(S(1));
        sim.LinkUp = true;
        sim.Run(S(1));

        Assert.Equal(0, sim.Count(CameraCommand.Hilight));
    }

    // Invariant: stop only ever comes from rule 2

    /// <summary>
    /// Random link drops, camera dropouts, busy flags, camera button presses, mark presses and
    /// REC flicks shorter than the hold time. No valid stop gesture ever happens, so no stop may be sent.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Stop_is_never_sent_without_a_valid_gesture(int seed)
    {
        var random = new Random(seed);
        var sim = Sim.Settled(random.Next(2) == 0 ? On : Off, encoding: random.Next(2) == 0);
        var recOffSince = (DateTimeOffset?)null;

        for (var i = 0; i < 20_000; i++)
        {
            switch (random.Next(200))
            {
                case 0: sim.LinkUp = !sim.LinkUp; break;
                case 1: sim.CameraOnline = !sim.CameraOnline; break;
                case 2: sim.Busy = !sim.Busy; break;
                case 3: sim.Encoding = !sim.Encoding; break;
                case 4: sim.Mark = sim.Mark == On ? Off : On; break;
                case 5: sim.CameraObeys = !sim.CameraObeys; break;
                case 6: sim.Rec = random.Next(3) switch { 0 => On, 1 => Off, _ => Mid }; break;
            }

            // Any OFF with a live link is cut short before the hold time, so it can never be a stop gesture.
            if (sim.Rec != On && sim.LinkUp)
            {
                recOffSince ??= sim.Now;
                if (sim.Now - recOffSince >= Ms(900))
                {
                    sim.Rec = On;
                    recOffSince = null;
                }
            }
            else if (sim.Rec == On)
            {
                recOffSince = null;
            }

            sim.Step();
        }

        Assert.Equal(0, sim.Count(CameraCommand.ShutterStop));
    }

    /// <summary>
    /// Fully random scenarios. Every stop sequence must begin right after a valid gesture: REC OFF for the
    /// whole hold time with the link up, after an ON seen while the link had been stable, with no outage since.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_stop_follows_a_valid_gesture(int seed)
    {
        var random = new Random(seed);
        var sim = Sim.Settled(On, encoding: true);
        var options = new BackpackOptions();

        // Independent model of what the receiver reported, tracked frame by frame.
        var lastFrame = sim.Now;
        var linkSince = sim.Now - S(3);    // Settled kept the link up for 3 s
        bool? previousRecOn = true;        // REC in the previous frame of this link period; null after an outage
        var recOn = true;                  // mid values keep the last state
        var lastOnWasStable = true;
        DateTimeOffset? offStart = null;   // start of the current OFF run, if it followed an ON
        var offRunValid = false;
        DateTimeOffset? lastValidGesture = null;

        for (var i = 0; i < 20_000; i++)
        {
            switch (random.Next(100))
            {
                case 0: sim.LinkUp = !sim.LinkUp; break;
                case 1: sim.CameraOnline = !sim.CameraOnline; break;
                case 2: sim.Encoding = !sim.Encoding; break;
                case 3: sim.CameraObeys = !sim.CameraObeys; break;
                case < 12: sim.Rec = random.Next(3) switch { 0 => On, 1 => Off, _ => Mid }; break;
            }

            var commands = sim.Step();
            var now = sim.Now;

            if (now - lastFrame >= options.LinkTimeout)
            {
                // Outage: the stop gesture has to start over once frames return.
                offStart = null;
                previousRecOn = null;
            }

            if (sim.LinkUp)
            {
                if (now - lastFrame >= options.LinkTimeout)
                    linkSince = now;
                lastFrame = now;

                if (sim.Rec != Mid)
                    recOn = sim.Rec == On;

                if (recOn)
                {
                    offStart = null;
                    lastValidGesture = null; // REC back ON ends any stop sequence
                    lastOnWasStable = now - linkSince >= options.LinkStableTime;
                }
                else if (offStart is null && previousRecOn == true)
                {
                    offStart = now;
                    offRunValid = lastOnWasStable;
                }

                previousRecOn = recOn;

                if (offStart is { } since && offRunValid && now - since >= options.StopHoldTime)
                    lastValidGesture = now;
            }

            // Every stop, including retries (at most 2 s after the commit), must come from a valid gesture.
            if (commands.Contains(CameraCommand.ShutterStop))
            {
                Assert.True(lastValidGesture is { } g && now - g <= S(2.05),
                    $"seed {seed}: stop at {now} without a valid gesture (last {lastValidGesture})");
            }
        }

        Assert.True(sim.Count(CameraCommand.ShutterStop) > 0, $"seed {seed}: scenario never stopped, test proves nothing");
    }

    public static TheoryData<int> Seeds() => new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
}
