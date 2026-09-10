plugins {
    alias(libs.plugins.kotlin.jvm)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
}

description = "Integration tests against the shared IntegrationTestServer (needs SIGNALARRR_TEST_SERVER_URL)"

kotlin {
    jvmToolchain(21)
}

dependencies {
    testImplementation(project(":signalarrr"))
    kspTest(project(":signalarrr-ksp"))
    testImplementation(libs.okhttp)
    testImplementation(platform(libs.junit.bom))
    testImplementation(libs.junit.jupiter)
    testImplementation(libs.kotlinx.coroutines.test)
    testRuntimeOnly(libs.junit.platform.launcher)
}

tasks.test {
    useJUnitPlatform()
    // The server URL is discovered by scripts/test-server.sh; tests skip themselves without it.
    environment("SIGNALARRR_TEST_SERVER_URL", System.getenv("SIGNALARRR_TEST_SERVER_URL") ?: "")
    systemProperty("junit.jupiter.execution.parallel.enabled", "false")
    testLogging {
        events("passed", "failed", "skipped")
        exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL
        showStandardStreams = false
    }
    outputs.upToDateWhen { false }
}
