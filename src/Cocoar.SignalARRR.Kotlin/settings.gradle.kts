pluginManagement {
    repositories {
        gradlePluginPortal()
        mavenCentral()
    }
}

dependencyResolutionManagement {
    repositories {
        mavenCentral()
    }
}

rootProject.name = "cocoar-signalarrr"

include(":signalarrr")
include(":signalarrr-ksp")
include(":signalarrr-integration-tests")
