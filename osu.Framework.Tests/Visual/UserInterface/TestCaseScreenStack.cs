﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.MathUtils;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Graphics;

namespace osu.Framework.Tests.Visual.UserInterface
{
    public class TestCaseScreenStack : TestCase
    {
        private TestScreen baseScreen;
        private ScreenStack stack;

        public override IReadOnlyList<Type> RequiredTypes => new[]
        {
            typeof(Screen),
            typeof(IScreen)
        };

        [SetUp]
        public void SetupTest() => Schedule(() =>
        {
            Clear();
            Add(stack = new ScreenStack(baseScreen = new TestScreen())
            {
                RelativeSizeAxes = Axes.Both
            });
        });

        [Test]
        public void TestPushFocusLost()
        {
            TestScreen screen1 = null;

            pushAndEnsureCurrent(() => screen1 = new TestScreen { EagerFocus = true });
            AddUntilStep("wait for focus grab", () => GetContainingInputManager().FocusedDrawable == screen1);

            pushAndEnsureCurrent(() => new TestScreen(), () => screen1);

            AddUntilStep("focus lost", () => GetContainingInputManager().FocusedDrawable != screen1);
        }

        [Test]
        public void TestPushFocusTransferred()
        {
            TestScreen screen1 = null, screen2 = null;

            pushAndEnsureCurrent(() => screen1 = new TestScreen { EagerFocus = true });
            AddUntilStep("wait for focus grab", () => GetContainingInputManager().FocusedDrawable == screen1);

            pushAndEnsureCurrent(() => screen2 = new TestScreen { EagerFocus = true }, () => screen1);

            AddUntilStep("focus transferred", () => GetContainingInputManager().FocusedDrawable == screen2);
        }

        [Test]
        public void TestPushStackTwice()
        {
            TestScreen testScreen = null;

            AddStep("public push", () => stack.Push(testScreen = new TestScreen()));
            AddStep("ensure succeeds", () => Assert.IsTrue(stack.CurrentScreen == testScreen));
            AddStep("ensure internal throws", () => Assert.Throws<InvalidOperationException>(() => stack.Push(null, new TestScreen())));
        }

        [Test]
        public void TestAddScreenWithoutStackFails()
        {
            AddStep("ensure throws", () => Assert.Throws<InvalidOperationException>(() => Add(new TestScreen())));
        }

        [Test]
        public void TestPushInstantExitScreen()
        {
            AddStep("push non-valid screen", () => baseScreen.Push(new TestScreen { ValidForPush = false }));
            AddAssert("stack is single", () => stack.InternalChildren.Count == 1);
        }

        [Test]
        public void TestPushInstantExitScreenEmpty()
        {
            AddStep("fresh stack with non-valid screen", () =>
            {
                Clear();
                Add(stack = new ScreenStack(baseScreen = new TestScreen { ValidForPush = false })
                {
                    RelativeSizeAxes = Axes.Both
                });
            });

            AddAssert("stack is empty", () => stack.InternalChildren.Count == 0);
        }

        [Test]
        public void TestPushPop()
        {
            TestScreen screen1 = null, screen2 = null;

            pushAndEnsureCurrent(() => screen1 = new TestScreen());

            AddAssert("baseScreen suspended to screen1", () => baseScreen.SuspendedTo == screen1);
            AddAssert("screen1 entered from baseScreen", () => screen1.EnteredFrom == baseScreen);

            // we don't support pushing a screen that has been entered
            AddStep("bad push", () => Assert.Throws(typeof(ScreenStack.ScreenAlreadyEnteredException), () => screen1.Push(screen1)));

            pushAndEnsureCurrent(() => screen2 = new TestScreen(), () => screen1);

            AddAssert("screen1 suspended to screen2", () => screen1.SuspendedTo == screen2);
            AddAssert("screen2 entered from screen1", () => screen2.EnteredFrom == screen1);

            AddAssert("ensure child", () => screen1.GetChildScreen() != null);

            AddStep("pop", () => screen2.Exit());

            AddAssert("screen1 resumed from screen2", () => screen1.ResumedFrom == screen2);
            AddAssert("screen2 exited to screen1", () => screen2.ExitedTo == screen1);
            AddAssert("screen2 has lifetime end", () => screen2.LifetimeEnd != double.MaxValue);

            AddAssert("ensure child gone", () => screen1.GetChildScreen() == null);
            AddAssert("ensure not current", () => !screen2.IsCurrentScreen());

            AddStep("pop", () => screen1.Exit());

            AddAssert("baseScreen resumed from screen1", () => baseScreen.ResumedFrom == screen1);
            AddAssert("screen1 exited to baseScreen", () => screen1.ExitedTo == baseScreen);
            AddAssert("screen1 has lifetime end", () => screen1.LifetimeEnd != double.MaxValue);
            AddUntilStep("screen1 is removed", () => screen1.Parent == null);
        }

        [Test]
        public void TestMultiLevelExit()
        {
            TestScreen screen1 = null, screen2 = null, screen3 = null;

            pushAndEnsureCurrent(() => screen1 = new TestScreen());
            pushAndEnsureCurrent(() => screen2 = new TestScreen { ValidForResume = false }, () => screen1);
            pushAndEnsureCurrent(() => screen3 = new TestScreen(), () => screen2);

            AddStep("bad exit", () => Assert.Throws(typeof(ScreenStack.ScreenHasChildException), () => screen1.Exit()));
            AddStep("exit", () => screen3.Exit());

            AddAssert("screen3 exited to screen2", () => screen3.ExitedTo == screen2);
            AddAssert("screen2 not resumed from screen3", () => screen2.ResumedFrom == null);
            AddAssert("screen2 exited to screen1", () => screen2.ExitedTo == screen1);
            AddAssert("screen1 resumed from screen2", () => screen1.ResumedFrom == screen2);

            AddAssert("screen3 has lifetime end", () => screen3.LifetimeEnd != double.MaxValue);
            AddAssert("screen2 has lifetime end", () => screen2.LifetimeEnd != double.MaxValue);
            AddAssert("screen 2 is not alive", () => !screen2.AsDrawable().IsAlive);

            AddAssert("ensure child gone", () => screen1.GetChildScreen() == null);
            AddAssert("ensure current", () => screen1.IsCurrentScreen());

            AddAssert("ensure not current", () => !screen2.IsCurrentScreen());
            AddAssert("ensure not current", () => !screen3.IsCurrentScreen());
        }

        [Test]
        public void TestAsyncPush()
        {
            TestScreenSlow screen1 = null;

            AddStep("push slow", () => baseScreen.Push(screen1 = new TestScreenSlow()));
            AddAssert("base screen registered suspend", () => baseScreen.SuspendedTo == screen1);
            AddAssert("ensure not current", () => !screen1.IsCurrentScreen());
            AddStep("allow load", () => screen1.AllowLoad = true);
            AddUntilStep("ensure current", () => screen1.IsCurrentScreen());
        }

        [Test]
        public void TestAsyncPreloadPush()
        {
            TestScreenSlow screen1 = null;
            AddStep("preload slow", () => LoadComponentAsync(screen1 = new TestScreenSlow { AllowLoad = true }));
            pushAndEnsureCurrent(() => screen1);
        }

        [Test]
        public void TestExitBeforePush()
        {
            TestScreenSlow screen1 = null;
            TestScreen screen2 = null;

            AddStep("push slow", () => baseScreen.Push(screen1 = new TestScreenSlow()));
            AddStep("exit slow", () => screen1.Exit());
            AddStep("allow load", () => screen1.AllowLoad = true);
            AddUntilStep("wait for screen to load", () => screen1.LoadState >= LoadState.Ready);
            AddAssert("ensure not current", () => !screen1.IsCurrentScreen());
            AddAssert("ensure base still current", () => baseScreen.IsCurrentScreen());
            AddStep("push fast", () => baseScreen.Push(screen2 = new TestScreen()));
            AddUntilStep("ensure new current", () => screen2.IsCurrentScreen());
        }

        [Test]
        public void TestPushToNonLoadedScreenFails()
        {
            TestScreenSlow screen1 = null;

            AddStep("push slow", () => stack.Push(screen1 = new TestScreenSlow()));
            AddStep("push second slow", () => Assert.Throws<InvalidOperationException>(() => screen1.Push(new TestScreenSlow())));
        }

        [Test]
        public void TestEventOrder()
        {
            List<int> order = new List<int>();

            var screen1 = new TestScreen
            {
                Entered = () => order.Add(1),
                Suspended = () => order.Add(2),
                Resumed = () => order.Add(5),
            };

            var screen2 = new TestScreen
            {
                Entered = () => order.Add(3),
                Exited = () => order.Add(4),
            };

            AddStep("push screen1", () => stack.Push(screen1));
            AddUntilStep("ensure current", () => screen1.IsCurrentScreen());

            AddStep("preload screen2", () => LoadComponentAsync(screen2));
            AddUntilStep("wait for load", () => screen2.LoadState == LoadState.Ready);

            AddStep("push screen2", () => screen1.Push(screen2));
            AddUntilStep("ensure current", () => screen2.IsCurrentScreen());

            AddStep("exit screen2", () => screen2.Exit());
            AddUntilStep("ensure exited", () => !screen2.IsCurrentScreen());

            AddStep("push screen2", () => screen1.Exit());
            AddUntilStep("ensure exited", () => !screen1.IsCurrentScreen());

            AddAssert("order is correct", () => order.SequenceEqual(order.OrderBy(i => i)));
        }

        [Test]
        public void TestComeVisibleFromHidden()
        {
            TestScreen screen1 = null;
            pushAndEnsureCurrent(() => screen1 = new TestScreen { Alpha = 0 });

            AddUntilStep("screen1 is visible", () => screen1.Alpha > 0);

            pushAndEnsureCurrent(() => new TestScreen { Alpha = 0 }, () => screen1);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TestAsyncEventOrder(bool earlyExit, bool suspendImmediately)
        {
            if (!suspendImmediately)
            {
                AddStep("override stack", () =>
                {
                    // we can't use the [SetUp] screen stack as we need to change the ctor parameters.
                    Clear();
                    Add(stack = new ScreenStack(baseScreen = new TestScreen(), suspendImmediately: false)
                    {
                        RelativeSizeAxes = Axes.Both
                    });
                });
            }

            List<int> order = new List<int>();

            var screen1 = new TestScreenSlow
            {
                Entered = () => order.Add(1),
                Suspended = () => order.Add(2),
                Resumed = () => order.Add(5),
            };

            var screen2 = new TestScreenSlow
            {
                Entered = () => order.Add(3),
                Exited = () => order.Add(4),
            };

            AddStep("push slow", () => stack.Push(screen1));
            AddStep("push second slow", () => stack.Push(screen2));

            AddStep("allow load 1", () => screen1.AllowLoad = true);

            AddUntilStep("ensure screen1 not current", () => !screen1.IsCurrentScreen());
            AddUntilStep("ensure screen2 not current", () => !screen2.IsCurrentScreen());

            // but the stack has a different idea of "current"
            AddAssert("ensure screen2 is current at the stack", () => stack.CurrentScreen == screen2);

            if (suspendImmediately)
                AddUntilStep("screen1's suspending fired", () => screen1.SuspendedTo == screen2);
            else
                AddUntilStep("screen1's entered and suspending fired", () => screen1.EnteredFrom != null);

            if (earlyExit)
                AddStep("early exit 2", () => screen2.Exit());

            AddStep("allow load 2", () => screen2.AllowLoad = true);

            if (earlyExit)
            {
                AddAssert("screen2's entered did not fire", () => screen2.EnteredFrom == null);
                AddAssert("screen2's exited did not fire", () => screen2.ExitedTo == null);
            }
            else
            {
                AddUntilStep("ensure screen2 is current", () => screen2.IsCurrentScreen());
                AddAssert("screen2's entered fired", () => screen2.EnteredFrom == screen1);
                AddStep("exit 2", () => screen2.Exit());
                AddUntilStep("ensure screen1 is current", () => screen1.IsCurrentScreen());
                AddAssert("screen2's exited fired", () => screen2.ExitedTo == screen1);
            }

            AddAssert("order is correct", () => order.SequenceEqual(order.OrderBy(i => i)));
        }

        [Test]
        public void TestAsyncDoublePush()
        {
            TestScreenSlow screen1 = null;
            TestScreenSlow screen2 = null;

            AddStep("push slow", () => stack.Push(screen1 = new TestScreenSlow()));
            // important to note we are pushing to the stack here, unlike the failing case above.
            AddStep("push second slow", () => stack.Push(screen2 = new TestScreenSlow()));

            AddAssert("base screen registered suspend", () => baseScreen.SuspendedTo == screen1);

            AddAssert("screen1 is not current", () => !screen1.IsCurrentScreen());
            AddAssert("screen2 is not current", () => !screen2.IsCurrentScreen());

            AddAssert("screen2 is current to stack", () => stack.CurrentScreen == screen2);

            AddAssert("screen1 not registered suspend", () => screen1.SuspendedTo == null);
            AddAssert("screen2 not registered entered", () => screen2.EnteredFrom == null);

            AddStep("allow load 2", () => screen2.AllowLoad = true);

            // screen 2 won't actually be loading since the load is only triggered after screen1 is loaded.
            AddWaitStep("wait for load", 10);

            // furthermore, even though screen 2 is able to load, screen 1 has not yet so we shouldn't has received any events.
            AddAssert("screen1 is not current", () => !screen1.IsCurrentScreen());
            AddAssert("screen2 is not current", () => !screen2.IsCurrentScreen());
            AddAssert("screen1 not registered suspend", () => screen1.SuspendedTo == null);
            AddAssert("screen2 not registered entered", () => screen2.EnteredFrom == null);

            AddStep("allow load 1", () => screen1.AllowLoad = true);
            AddUntilStep("screen1 is loaded", () => screen1.LoadState == LoadState.Loaded);
            AddUntilStep("screen2 is loaded", () => screen2.LoadState == LoadState.Loaded);

            AddUntilStep("screen1 is expired", () => !screen1.IsAlive);

            AddUntilStep("screen1 is not current", () => !screen1.IsCurrentScreen());
            AddUntilStep("screen2 is current", () => screen2.IsCurrentScreen());

            AddAssert("screen1 registered suspend", () => screen1.SuspendedTo == screen2);
            AddAssert("screen2 registered entered", () => screen2.EnteredFrom == screen1);
        }

        [Test]
        public void TestAsyncPushWithNonImmediateSuspend()
        {
            AddStep("override stack", () =>
            {
                // we can't use the [SetUp] screen stack as we need to change the ctor parameters.
                Clear();
                Add(stack = new ScreenStack(baseScreen = new TestScreen(), suspendImmediately: false)
                {
                    RelativeSizeAxes = Axes.Both
                });
            });

            TestScreenSlow screen1 = null;

            AddStep("push slow", () => baseScreen.Push(screen1 = new TestScreenSlow()));
            AddAssert("base screen not yet registered suspend", () => baseScreen.SuspendedTo == null);
            AddAssert("ensure notcurrent", () => !screen1.IsCurrentScreen());
            AddStep("allow load", () => screen1.AllowLoad = true);
            AddUntilStep("ensure current", () => screen1.IsCurrentScreen());
            AddAssert("base screen registered suspend", () => baseScreen.SuspendedTo == screen1);
        }

        [Test]
        public void TestMakeCurrent()
        {
            TestScreen screen1 = null;
            TestScreen screen2 = null;
            TestScreen screen3 = null;

            pushAndEnsureCurrent(() => screen1 = new TestScreen());
            pushAndEnsureCurrent(() => screen2 = new TestScreen(), () => screen1);
            pushAndEnsureCurrent(() => screen3 = new TestScreen(), () => screen2);

            AddStep("block exit", () => screen3.Exiting = () => true);
            AddStep("make screen 1 current", () => screen1.MakeCurrent());
            AddAssert("screen 3 still current", () => screen3.IsCurrentScreen());
            AddAssert("screen 3 doesn't have lifetime end", () => screen3.LifetimeEnd == double.MaxValue);
            AddAssert("screen 2 valid for resume", () => screen2.ValidForResume);
            AddAssert("screen 1 valid for resume", () => screen1.ValidForResume);

            AddStep("don't block exit", () => screen3.Exiting = () => false);
            AddStep("make screen 1 current", () => screen1.MakeCurrent());
            AddAssert("screen 1 current", () => screen1.IsCurrentScreen());
            AddAssert("screen 1 doesn't have lifetime end", () => screen1.LifetimeEnd == double.MaxValue);
            AddAssert("screen 3 has lifetime end", () => screen3.LifetimeEnd != double.MaxValue);
            AddAssert("screen 2 is not alive", () => !screen2.AsDrawable().IsAlive);
        }

        [Test]
        public void TestMakeCurrentUnbindOrder()
        {
            List<TestScreen> screens = new List<TestScreen>();

            for (int i = 0; i < 5; i++)
            {
                var screen = new TestScreen();
                var target = screens.LastOrDefault();

                screen.OnUnbind += () =>
                {
                    if (screens.Last() != screen)
                        throw new InvalidOperationException("Disposal order was wrong");
                    screens.Remove(screen);
                };

                pushAndEnsureCurrent(() => screen, target != null ? () => target : (Func<IScreen>)null);
                screens.Add(screen);
            }

            AddStep("make first screen current", () => screens.First().MakeCurrent());
            AddUntilStep("All screens disposed in correct order", () => screens.Count == 1);
        }

        /// <summary>
        /// Make sure that all bindables are returned before OnResuming is called for the next screen.
        /// </summary>
        [Test]
        public void TestReturnBindsBeforeResume()
        {
            TestScreen screen1 = null, screen2 = null;
            pushAndEnsureCurrent(() => screen1 = new TestScreen());
            pushAndEnsureCurrent(() => screen2 = new TestScreen(true), () => screen1);
            AddStep("Exit screen", () => screen2.Exit());
            AddUntilStep("Wait until base is current", () => screen1.IsCurrentScreen());
            AddAssert("Bindables have been returned by new screen", () => !screen2.DummyBindable.Disabled && !screen2.LeasedCopy.Disabled);
        }

        private void pushAndEnsureCurrent(Func<IScreen> screenCtor, Func<IScreen> target = null)
        {
            IScreen screen = null;
            AddStep("push", () => (target?.Invoke() ?? baseScreen).Push(screen = screenCtor()));
            AddUntilStep("ensure current", () => screen.IsCurrentScreen());
        }

        private class TestScreenSlow : TestScreen
        {
            public bool AllowLoad;

            [BackgroundDependencyLoader]
            private void load()
            {
                while (!AllowLoad)
                    Thread.Sleep(10);
            }
        }

        private class TestScreen : Screen
        {
            public Func<bool> Exiting;

            public Action Entered;
            public Action Suspended;
            public Action Resumed;
            public Action Exited;

            public IScreen EnteredFrom;
            public IScreen ExitedTo;

            public IScreen SuspendedTo;
            public IScreen ResumedFrom;

            public static int Sequence;
            private Button popButton;

            private const int transition_time = 500;

            public bool EagerFocus;

            public override bool RequestsFocus => EagerFocus;

            public override bool AcceptsFocus => EagerFocus;

            public override bool HandleNonPositionalInput => true;
            public Action OnUnbind;

            public LeasedBindable<bool> LeasedCopy;

            public readonly Bindable<bool> DummyBindable = new Bindable<bool>();

            private readonly bool shouldTakeOutLease;

            internal override void UnbindAllBindables()
            {
                base.UnbindAllBindables();
                OnUnbind?.Invoke();
            }

            public TestScreen(bool shouldTakeOutLease = false)
            {
                this.shouldTakeOutLease = shouldTakeOutLease;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = new Color4(
                            Math.Max(0.5f, RNG.NextSingle()),
                            Math.Max(0.5f, RNG.NextSingle()),
                            Math.Max(0.5f, RNG.NextSingle()),
                            1),
                    },
                    new SpriteText
                    {
                        Text = $@"Screen {Sequence++}",
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = new FontUsage(size: 50)
                    },
                    popButton = new Button
                    {
                        Text = @"Pop",
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.1f),
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        BackgroundColour = Color4.Red,
                        Alpha = 0,
                        Action = this.Exit
                    },
                    new Button
                    {
                        Text = @"Push",
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.1f),
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        BackgroundColour = Color4.YellowGreen,
                        Action = delegate
                        {
                            this.Push(new TestScreen
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            });
                        }
                    }
                };

                BorderColour = Color4.Red;
                Masking = true;
            }

            protected override void OnFocus(FocusEvent e)
            {
                base.OnFocus(e);
                BorderThickness = 10;
            }

            protected override void OnFocusLost(FocusLostEvent e)
            {
                base.OnFocusLost(e);
                BorderThickness = 0;
            }

            public override void OnEntering(IScreen last)
            {
                EnteredFrom = last;
                Entered?.Invoke();

                if (shouldTakeOutLease)
                {
                    DummyBindable.BindTo(((TestScreen)last).DummyBindable);
                    LeasedCopy = DummyBindable.BeginLease(true);
                }

                base.OnEntering(last);

                if (last != null)
                {
                    //only show the pop button if we are entered form another screen.
                    popButton.Alpha = 1;
                }

                this.MoveTo(new Vector2(0, -DrawSize.Y));
                this.MoveTo(Vector2.Zero, transition_time, Easing.OutQuint);
                this.FadeIn(1000);
            }

            public override bool OnExiting(IScreen next)
            {
                ExitedTo = next;
                Exited?.Invoke();

                if (Exiting?.Invoke() == true)
                    return true;

                this.MoveTo(new Vector2(0, -DrawSize.Y), transition_time, Easing.OutQuint);
                return base.OnExiting(next);
            }

            public override void OnSuspending(IScreen next)
            {
                SuspendedTo = next;
                Suspended?.Invoke();

                base.OnSuspending(next);
                this.MoveTo(new Vector2(0, DrawSize.Y), transition_time, Easing.OutQuint);
            }

            public override void OnResuming(IScreen last)
            {
                ResumedFrom = last;
                Resumed?.Invoke();

                base.OnResuming(last);
                this.MoveTo(Vector2.Zero, transition_time, Easing.OutQuint);
            }
        }
    }
}