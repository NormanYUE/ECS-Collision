using Ember;

// 显式 Burst 策略：碰撞管线的全部 Job 要求生成 Burst 调度器。
// Unity.Burst 不可用或调度器无法解析时在编译期 / BuildAccess 显式失败，绝不静默回退托管调度。
[assembly: EmberJobCompilation(EmberJobCompilationMode.Burst)]
