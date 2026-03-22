// swift-tools-version: 5.10
import PackageDescription
import CompilerPluginSupport

let package = Package(
    name: "CocoarSignalARRR",
    platforms: [.macOS(.v11), .iOS(.v14), .tvOS(.v14), .watchOS(.v7)],
    products: [
        .library(name: "CocoarSignalARRR", targets: ["CocoarSignalARRR"]),
        .library(name: "CocoarSignalARRRMacros", targets: ["CocoarSignalARRRClient"]),
    ],
    dependencies: [
        .package(url: "https://github.com/dotnet/signalr-client-swift.git", from: "1.0.0-preview.1"),
        .package(url: "https://github.com/swiftlang/swift-syntax.git", from: "510.0.0"),
    ],
    targets: [
        .target(
            name: "CocoarSignalARRR",
            dependencies: [.product(name: "SignalRClient", package: "signalr-client-swift")],
            path: "src/Cocoar.SignalARRR.Swift/Sources/CocoarSignalARRR"
        ),
        .macro(
            name: "CocoarSignalARRRMacroPlugin",
            dependencies: [
                .product(name: "SwiftSyntaxMacros", package: "swift-syntax"),
                .product(name: "SwiftCompilerPlugin", package: "swift-syntax"),
            ],
            path: "src/Cocoar.SignalARRR.Swift/Sources/CocoarSignalARRRMacroPlugin"
        ),
        .target(
            name: "CocoarSignalARRRClient",
            dependencies: ["CocoarSignalARRR", "CocoarSignalARRRMacroPlugin"],
            path: "src/Cocoar.SignalARRR.Swift/Sources/CocoarSignalARRRClient"
        ),
        .testTarget(
            name: "CocoarSignalARRRTests",
            dependencies: ["CocoarSignalARRR"],
            path: "src/Cocoar.SignalARRR.Swift/Tests/CocoarSignalARRRTests"
        ),
        .testTarget(
            name: "CocoarSignalARRRMacroTests",
            dependencies: [
                "CocoarSignalARRRMacroPlugin",
                "CocoarSignalARRRClient",
                .product(name: "SwiftSyntaxMacrosTestSupport", package: "swift-syntax"),
            ],
            path: "src/Cocoar.SignalARRR.Swift/Tests/CocoarSignalARRRMacroTests"
        ),
        .testTarget(
            name: "CocoarSignalARRRIntegrationTests",
            dependencies: ["CocoarSignalARRR"],
            path: "src/Cocoar.SignalARRR.Swift/Tests/CocoarSignalARRRIntegrationTests"
        ),
    ]
)
