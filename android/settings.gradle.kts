// LapCont — Android — Phase 0 modules and repositories
// License: MIT
pluginManagement { repositories { google(); mavenCentral(); gradlePluginPortal() } }
dependencyResolutionManagement { repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS); repositories { google(); mavenCentral() } }
rootProject.name = "LapCont"
include(":transport", ":app", ":mobile", ":push", ":qa-smoke")
