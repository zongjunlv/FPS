# Issue tracker: GitHub

本项目的开发任务和 PRD 使用 GitHub Issues 管理，仓库为 `zongjunlv/FPS`。所有操作使用 `gh` CLI，并从仓库的 Git Remote 自动解析目标。

## 约定

- 创建、读取、评论、标记和关闭任务均操作 GitHub Issue。
- 经审核的开发任务按依赖顺序创建，确保阻塞关系引用真实 Issue 编号。
- 信息完整、可由代理独立实现的任务使用 `ready-for-agent` 标签。
- 不关闭或修改作为需求来源的父 Issue。

## Pull Request 分诊

外部 Pull Request 不作为需求或分诊入口。分诊流程只处理 GitHub Issues，不把 PR 混入 Issue 队列。

## 技能术语

- 技能要求“发布到 Issue Tracker”时，创建 GitHub Issue。
- 技能要求“读取 Ticket”时，读取对应 GitHub Issue 的正文、评论和标签。
