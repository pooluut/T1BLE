# Issue tracker: Local Markdown

本仓库的任务和规格保存在 `.scratch/` 中。

## 文件约定

- 每项功能使用一个目录：`.scratch/<feature-slug>/`。
- 规格文件：`.scratch/<feature-slug>/spec.md`。
- 每个实施任务独立保存：
  `.scratch/<feature-slug>/issues/<NN>-<slug>.md`。
- 编号从 `01` 开始，不将多个任务合并为一个文件。
- 任务文件顶部使用 `Status:` 记录分类状态，
  取值见 `triage-labels.md`。
- 评论和讨论追加到文件末尾的 `## Comments` 下。

## 发布和读取任务

当技能要求“发布到任务跟踪系统”时，
在对应功能目录中创建文件，按需创建目录。

当技能要求“获取相关任务”时，读取指定的任务文件。
只有编号时，在对应功能的 issues 目录中查找；
无法唯一定位时，向用户确认。

## Wayfinder 操作

- 工作地图：`.scratch/<effort>/map.md`，
  包含 Notes、Decisions-so-far 和 Fog。
- 子任务：`.scratch/<effort>/issues/NN-<slug>.md`，
  编号从 `01` 开始，正文记录待解答的问题。
- `Type:` 取值为 research、prototype、grilling 或 task。
- Wayfinder 子任务使用独立的执行状态：
  `Status: open`、`Status: claimed` 或 `Status: resolved`。
- 依赖写在顶部的 `Blocked by: NN, NN` 中；
  所有依赖均为 resolved 时，任务才解除阻塞。
- 选择编号最小、状态为 open 且无阻塞的子任务。
- 开始工作前，将状态改为 claimed 并保存。
- 完成后，在 `## Answer` 下追加答案，将状态改为 resolved，
  并在地图的 Decisions-so-far 中追加结论摘要和文件链接。
