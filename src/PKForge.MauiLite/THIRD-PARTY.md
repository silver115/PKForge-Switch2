# Third-party code in MauiLite

MauiLite ports parts of [.NET MAUI](https://github.com/dotnet/maui), tag `10.0.20`, so that layouts come out as they do in PKForge's Android app.

**Licence:** MIT. Copyright (c) .NET Foundation and Contributors. The licence text is at https://github.com/dotnet/maui/blob/10.0.20/LICENSE.txt.

Each ported file starts with a comment naming its source.

| MauiLite file | MAUI source | Changes |
|---|---|---|
| `Primitives/Dimension.cs` | `src/Core/src/Primitives/Dimension.cs` | none |
| `Primitives/GridUnitType.cs` | `src/Core/src/Primitives/GridUnitType.cs` | none |
| `Primitives/LayoutAlignment.cs` | `src/Core/src/Primitives/LayoutAlignment.cs` | none |
| `Primitives/Visibility.cs` | `src/Core/src/Primitives/Visibility.cs` | none |
| `Primitives/Thickness.cs` | `src/Core/src/Primitives/Thickness.cs` | type-converter attribute removed |
| `Primitives/CornerRadius.cs` | `src/Core/src/Primitives/CornerRadius.cs` | type-converter attribute removed |
| `Primitives/GridLength.cs` | `src/Core/src/Primitives/GridLength.cs` | type-converter attribute removed |
| `Layouts/Flex.cs` | `src/Core/src/Layouts/Flex.cs` | none |
| `Layouts/FlexEnums.cs` | `src/Core/src/Layouts/FlexEnums.cs` | type-converter attributes removed |
| `Layouts/FlexLayoutManager.cs` | `src/Core/src/Layouts/FlexLayoutManager.cs` | none |
| `Layouts/GridLayoutManager.cs` | `src/Core/src/Layouts/GridLayoutManager.cs` | none |
| `Layouts/HorizontalStackLayoutManager.cs` | `src/Core/src/Layouts/HorizontalStackLayoutManager.cs` | none |
| `Layouts/ILayoutManager.cs` | `src/Core/src/Layouts/ILayoutManager.cs` | none |
| `Layouts/LayoutExtensions.cs` | `src/Core/src/Layouts/LayoutExtensions.cs` | `ComputeDesiredSize` removed (it measures through a platform handler; MauiLite measures in `VisualElement`) |
| `Layouts/LayoutManager.cs` | `src/Core/src/Layouts/LayoutManager.cs` | none |
| `Layouts/StackLayoutManager.cs` | `src/Core/src/Layouts/StackLayoutManager.cs` | none |
| `Layouts/VerticalStackLayoutManager.cs` | `src/Core/src/Layouts/VerticalStackLayoutManager.cs` | none |
| `Controls/FlexLayout.cs` | `src/Controls/src/Core/Layout/FlexLayout.cs` | `ContentProperty` attribute and `Controls.Internals` using removed |
| `Controls/FlexExtensions.cs` | `src/Controls/src/Core/Layout/FlexExtensions.cs` | none |

`Layouts/Interfaces.cs` declares MAUI's Core interfaces (`IView`, `ILayout`, `IGridLayout` and others), cut to the members the ported managers use.

`Controls/VisualElement.cs` follows MAUI's Android measure rules (`ContextExtensions.CreateMeasureSpec` and Android's `View.getDefaultSize`). It does not copy their code.
