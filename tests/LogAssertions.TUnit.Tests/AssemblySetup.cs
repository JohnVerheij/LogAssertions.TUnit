// Lets the failure dumps and the tee see when each test ends (see LogAssertionsTestLifetimeAttribute). The smoke-test
// project runs without it, to pin the behavior of a consumer that has not added it.
[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]
