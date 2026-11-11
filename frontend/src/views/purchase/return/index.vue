<script setup lang="ts">
const title = "return";
</script>

<template>
  <el-card shadow="never">
    <div class="placeholder">
      <h3>{{ title }}</h3>
      <p>该页面正在建设中。</p>
    </div>
  </el-card>
</template>

<style scoped>
.placeholder {
  padding: 60px 0;
  text-align: center;
  color: var(--el-text-color-secondary);
}
</style>
