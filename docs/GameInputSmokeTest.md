# Microsoft.GameInput smoke test

1. Connect one or more controllers before launching the app.
2. Build and run the app on Windows.
3. Confirm that the dashboard defaults to `Microsoft.GameInput` instead of demo input.
4. Check the `Detected controllers` card.
5. Open the `Controller` combo box and verify that connected controllers are listed.
6. Select a controller when multiple controllers are connected.
7. Move sticks, triggers, and buttons on the selected controller.
8. Confirm that the physical controller surface updates in real time and the diagnostics tab shows changing raw values.
9. Switch to `Demo preview` and confirm that animation resumes even without hardware input.
10. Switch to `No live input` and confirm that the dashboard becomes idle.
