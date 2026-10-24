# TDD.md

Rules for test-driven development and unit tests in GameFlow.
The source is the Prooxie wiki "Unit Testing Guidelines for C#".
If a rule does not answer your question, ask the user.

## Workflow

Write the test first. Then write the code.

1. Write one test for one behaviour from the specification.
2. Run the test. Make sure it fails for the expected reason (red).
3. Write the minimum code that makes the test pass (green).
4. Refactor. Run all tests again. They must stay green.
5. Repeat for the next behaviour.

A task is complete when:

- every specified behaviour has a test,
- all tests pass,
- the build has no new warnings.

## Scope

These rules apply to unit tests at method and class level.
They do not apply to integration, UI, end-to-end, performance, or load tests.

## Framework

- Use xUnit v3 for tests. Use Moq for mocks.
- Run all tests with `dotnet test`.
- Run the tests before each commit. CI also runs them on each build.

## Frontend tests (Avalonia headless)

`TestAppBuilder.cs` sets this up with `[assembly: AvaloniaTestFramework]`.

- Use `[Fact]` and `[Theory]`. The framework runs them on the UI thread.
- Use the shared UI thread only. Never start a `HeadlessUnitTestSession`. A second session breaks the dispatcher of all tests.
- Put a class that calls `Dispatcher.UIThread.RunJobs()` in `[Collection(DispatcherCollection.Name)]`.
- Put a class that builds a control from XAML in `[Collection(AvaloniaViewCollection.Name)]`.
- Put a class that uses the static `LocalizationService` in `[Collection(LocalizationCollection.Name)]`.
- Add `IClassFixture<ReactiveUIFixture>` when the code under test uses ReactiveUI.

The error "The calling thread cannot access this object" means code ran on a different thread.
Find that code and move it to the UI thread. A rerun is not a fix.

## Project structure

- Put tests in `tests/<layer>/`, the mirror of `src/<layer>/`.
- Name each test project after its production project with the suffix `.Tests`: `Gameflow.Domain.Tests`.
- Mirror the folders of the production project in the test project.
- Mirror the namespaces: `Gameflow.Backend.Controllers` becomes `Gameflow.Backend.Tests.Controllers`.

Example:

```
src/Domain/Gameflow.Domain/Services/ConfigService.cs
tests/Domain/Gameflow.Domain.Tests/Services/ConfigServiceTests.cs
```

## Naming

- Name each test class `<ClassUnderTest>Tests`: `ConfigServiceTests`. Do not use underscores.
- Name each test method `<Method><Scenario><ExpectedResult>`.
- Do not use underscores in test method names. The analyzer (CA1707) reports them.
- Add the suffix `Async` to each asynchronous test method.

| Test method name | Meaning |
|---|---|
| `AddTwoPositiveNumbersReturnsSum` | `Add` returns the sum of two positive numbers. |
| `DivideByZeroThrowsDivideByZeroException` | `Divide` throws when the divisor is zero. |
| `IsEvenOddNumberReturnsFalse` | `IsEven` returns `false` for an odd number. |
| `TriggerAsyncNoPayloadReturnsOkAsync` | `TriggerAsync` returns OK when there is no payload. |

## Test class structure

- Write one test class for each production class.
- Keep all unit tests of a class in its test class.
- Put shared setup in the constructor or in a private factory method.
- Follow the rules in `CODING.md`. The build checks test code with the same analyzers.

Each test method has three parts (AAA):

1. **Arrange.** Prepare the inputs, the mocks, and the start state.
2. **Act.** Call the method under test once.
3. **Assert.** Verify the result or the behaviour.

Separate the three parts with one empty line. Write no `// Arrange`, `// Act`, or `// Assert` comments.
Write a comment in a test only to tell why, as in production code.

```csharp
namespace Gameflow.Domain.Tests.Services;

/// <summary>Tests for <see cref="Calculator"/>.</summary>
public sealed class CalculatorTests
{
    [Fact]
    public void AddTwoPositiveNumbersReturnsSum()
    {
        int a = 3;
        int b = 7;

        int result = Calculator.Add(a, b);

        Assert.Equal(10, result);
    }
}
```


## xUnit attributes

| Attribute | Use |
|---|---|
| `[Fact]` | A test with no parameters for one scenario. |
| `[Theory]` | A test with parameters. Each data row is a separate test case. |
| `[InlineData(...)]` | Simple constant data for a `[Theory]`. |
| `[MemberData(...)]` | Complex or reusable data from a static property, method, or field. |
| `[ClassData(...)]` | Large data sets from a class that implements `IEnumerable<object[]>`. |

- Use `[Fact]` for one input and output combination.
- Use `[Theory]` when the same logic needs more inputs. Do not copy a test method for each input.

```csharp
[Theory]
[InlineData(2, true)]
[InlineData(3, false)]
[InlineData(0, true)]
public void IsEvenAnyIntegerReturnsExpected(int input, bool expected)
{
    bool result = Calculator.IsEven(input);

    Assert.Equal(expected, result);
}
```

## What to test

- Each public method with logic.
- Edge cases and limits: minimum and maximum values, `null`, and empty collections.
- Critical logic: calculations, decisions, and data transformations.
- Exceptions: invalid input, `null` parameters, and zero divisors.
- Each input scenario that changes the behaviour of the method.

## What not to test

- Auto-properties and accessors with no logic.
- .NET framework code and third-party libraries. Mock or isolate them.
- Private members. Test them through the public interface.

## Mocking

Use a mock to replace a dependency that is external, slow, or not deterministic.
Examples: databases, web services, file systems, devices, and clocks.

Use Moq to:

- isolate the unit under test,
- set return values or behaviour,
- simulate exceptions and edge conditions,
- verify that a method was called, and how many times.

To test a method that returns `void`, verify its side effects.
Verify the calls to the mocks or the change of state.

```csharp
[Fact]
public void CreateInvoiceSendsEmailToCustomer()
{
    Mock<IEmailSender> emailSender = new();
    InvoiceService service = new(emailSender.Object);
    string customerEmail = "customer@example.com";

    service.CreateInvoice(customerEmail);

    emailSender.Verify(sender => sender.SendEmail(customerEmail, "Invoice Created", "Your invoice is ready."), Times.Once);
}
```
