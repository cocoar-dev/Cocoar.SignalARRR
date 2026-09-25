import com.vanniktech.maven.publish.MavenPublishBaseExtension

plugins {
    alias(libs.plugins.kotlin.jvm) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.ksp) apply false
    alias(libs.plugins.maven.publish) apply false
}

allprojects {
    group = property("group") as String
    version = property("version") as String
}

// Shared Maven Central setup for the published modules. Each module sets its own artifact name and
// POM name; everything Central requires beyond that — license, developer, SCM, sources and javadoc
// jars, signatures — is configured here once.
subprojects {
    plugins.withId("com.vanniktech.maven.publish") {
        extensions.configure<MavenPublishBaseExtension> {
            publishToMavenCentral()
            // Signing needs the key, which only the release workflow has (signingInMemoryKey);
            // without it, publishToMavenLocal still works for trying a build locally.
            if (providers.gradleProperty("signingInMemoryKey").isPresent) {
                signAllPublications()
            }
            pom {
                description.set(provider { project.description })
                url.set("https://docs.cocoar.dev/signalarrr/")
                inceptionYear.set("2026")
                licenses {
                    license {
                        name.set("Apache-2.0")
                        url.set("https://www.apache.org/licenses/LICENSE-2.0")
                        distribution.set("repo")
                    }
                }
                developers {
                    developer {
                        id.set("cocoar-dev")
                        name.set("Bernhard Windisch")
                        url.set("https://github.com/cocoar-dev")
                    }
                }
                scm {
                    url.set("https://github.com/cocoar-dev/Cocoar.SignalARRR")
                    connection.set("scm:git:https://github.com/cocoar-dev/Cocoar.SignalARRR.git")
                    developerConnection.set("scm:git:ssh://git@github.com/cocoar-dev/Cocoar.SignalARRR.git")
                }
            }
        }
    }
}
